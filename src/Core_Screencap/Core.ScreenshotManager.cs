using alphaShot;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepisPlugins;
using Illusion.Game;
using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Shared;
using UnityEngine;
using UnityEngine.SceneManagement;
#if KK || KKS
using StrayTech;
#endif

namespace Screencap
{
    /// <summary>
    /// Plugin for taking high quality screenshots.
    /// </summary>
    public partial class ScreenshotManager : ScreenshotManagerBase
    {
        public static ScreenshotManager Instance { get; private set; }

        internal AlphaShot2 currentAlphaShot;

        /// <summary>
        /// Triggered before a screenshot is captured. For use by plugins adding screen effects incompatible with Screencap.
        /// </summary>
        public static new event Action OnPreCapture;
        /// <summary>
        /// Triggered after a screenshot is captured. For use by plugins adding screen effects incompatible with Screencap.
        /// </summary>
        public static new event Action OnPostCapture;

        #region Config properties

        public static ConfigEntry<int> CardDownscalingRate { get; private set; }

        protected override void InitializeSettings()
        {
            InitializeSharedSettings();

            CardDownscalingRate = Config.Bind(
                "Render Settings", "Card image upsampling ratio",
                3,
                new ConfigDescription("Capture character card images in a higher resolution and then downscale them to desired size. Prevents aliasing, perserves small details and gives a smoother result, but takes longer to create.", new AcceptableValueRange<int>(1, 4)));
        }

        #endregion

        protected void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            Directory.CreateDirectory(screenshotDir);

            InitializeSettings();

            ResolutionX.SettingChanged += (sender, args) => CaptureWidthBuffer = ResolutionX.Value.ToString();
            ResolutionY.SettingChanged += (sender, args) => CaptureHeightBuffer = ResolutionY.Value.ToString();

            LoadSavedResolutions();

            SceneManager.sceneLoaded += (s, a) => InstallSceenshotHandler();
            InstallSceenshotHandler();

            Hooks.InstallHooks();

            I360Render.Init();
        }

        private void InstallSceenshotHandler()
        {
            if (!Camera.main || !Camera.main.gameObject) return;
            currentAlphaShot = Camera.main.gameObject.GetOrAddComponent<AlphaShot2>();
        }

        /// <summary>
        /// Capture the screen into a texture based on supplied arguments. Remember to destroy the texture when done with it.
        /// Can return null if there no 3D camera was found to take the picture with.
        /// </summary>
        /// <param name="width">Width of the resulting capture, after downscaling</param>
        /// <param name="height">Height of the resulting capture, after downscaling</param>
        /// <param name="downscaling">How much to oversize and then downscale. 1 for none.</param>
        /// <param name="transparent">Should the capture be transparent</param>
        public Texture2D Capture(int width, int height, int downscaling, bool transparent)
        {
            if (currentAlphaShot == null)
            {
                Logger.LogDebug("Capture - No camera found");
                return null;
            }

            try { OnPreCapture?.Invoke(); }
            catch (Exception ex) { Logger.LogError(ex); }
            var capture = currentAlphaShot.CaptureTex(width, height, downscaling, transparent ? AlphaMode.rgAlpha : AlphaMode.None);
            try { OnPostCapture?.Invoke(); }
            catch (Exception ex) { Logger.LogError(ex); }

            return capture;
        }

        private IEnumerator TakeCharScreenshot(bool in3D)
        {
            if (currentAlphaShot == null)
            {
                Logger.Log(LogLevel.Message, "Can't render a screenshot here, try UI screenshot instead");
                yield break;
            }

            try { OnPreCapture?.Invoke(); }
            catch (Exception ex) { Logger.LogError(ex); }

#if EC || KKS
            var colorMask = FindObjectOfType<CameraEffectorColorMask>();
            var colorMaskDisabled = false;
            if (colorMask && colorMask.Enabled)
            {
                colorMaskDisabled = true;
                colorMask.Enabled = false;
            }
#endif

            if (!in3D)
            {
                yield return new WaitForEndOfFrame();
                var capture = currentAlphaShot.CaptureTex(ResolutionX.Value, ResolutionY.Value, DownscalingRate.Value, CaptureAlphaMode.Value);

                var filename = GetUniqueFilename(CaptureAlphaMode.Value == AlphaMode.None ? "Render" : "Alpha");
                File.WriteAllBytes(filename, EncodeToFile(capture));
                Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"Character screenshot saved to {filename}");

                Destroy(capture);
            }
            else
            {
                var targetTr = Camera.main.transform;

                ToggleCameraControllers(targetTr, false);
                Time.timeScale = 0.01f;
                yield return new WaitForEndOfFrame();

                targetTr.position += targetTr.right * EyeSeparation.Value / 2;
                // Let the game render at the new position
                yield return new WaitForEndOfFrame();
                var capture = currentAlphaShot.CaptureTex(ResolutionX.Value, ResolutionY.Value, DownscalingRate.Value, CaptureAlphaMode.Value);

                targetTr.position -= targetTr.right * EyeSeparation.Value;
                yield return new WaitForEndOfFrame();
                var capture2 = currentAlphaShot.CaptureTex(ResolutionX.Value, ResolutionY.Value, DownscalingRate.Value, CaptureAlphaMode.Value);

                targetTr.position += targetTr.right * EyeSeparation.Value / 2;

                ToggleCameraControllers(targetTr, true);
                Time.timeScale = 1;

                var result = FlipEyesIn3DCapture.Value ? StitchImages(capture, capture2, ImageSeparationOffset.Value) : StitchImages(capture2, capture, ImageSeparationOffset.Value);

                var filename = GetUniqueFilename("3D-Render");
                File.WriteAllBytes(filename, EncodeToFile(result));

                Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"3D Character screenshot saved to {filename}");

                Destroy(capture);
                Destroy(capture2);
                Destroy(result);
            }

#if EC || KKS
            if (colorMaskDisabled && colorMask) colorMask.Enabled = true;
#endif

            try { OnPostCapture?.Invoke(); }
            catch (Exception ex) { Logger.LogError(ex); }

            PlayCaptureSound();
        }

        private IEnumerator Take360Screenshot(bool in3D)
        {
            try { OnPreCapture?.Invoke(); }
            catch (Exception ex) { Logger.LogError(ex); }

            yield return new WaitForEndOfFrame();

            if (!in3D)
            {
                yield return new WaitForEndOfFrame();

                var output = I360Render.CaptureTex(Resolution360.Value);
                var capture = EncodeToXmpFile(output);

                var filename = GetUniqueFilename("360");
                File.WriteAllBytes(filename, capture);

                Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"360 screenshot saved to {filename}");

                Destroy(output);
            }
            else
            {
                var targetTr = Camera.main.transform;

                ToggleCameraControllers(targetTr, false);
                Time.timeScale = 0.01f;
                yield return new WaitForEndOfFrame();

                targetTr.position += targetTr.right * EyeSeparation.Value / 2;
                // Let the game render at the new position
                yield return new WaitForEndOfFrame();
                var capture = I360Render.CaptureTex(Resolution360.Value);

                targetTr.position -= targetTr.right * EyeSeparation.Value;
                yield return new WaitForEndOfFrame();
                var capture2 = I360Render.CaptureTex(Resolution360.Value);

                targetTr.position += targetTr.right * EyeSeparation.Value / 2;

                ToggleCameraControllers(targetTr, true);
                Time.timeScale = 1;

                // Overlap is useless for these so don't use
                var result = FlipEyesIn3DCapture.Value ? StitchImages(capture, capture2, 0) : StitchImages(capture2, capture, 0);

                var filename = GetUniqueFilename("3D-360");
                File.WriteAllBytes(filename, EncodeToXmpFile(result));

                Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"3D 360 screenshot saved to {filename}");

                Destroy(result);
                Destroy(capture);
                Destroy(capture2);
            }

            try { OnPostCapture?.Invoke(); }
            catch (Exception ex) { Logger.LogError(ex); }

            PlayCaptureSound();
        }

        /// <summary>
        /// Need to disable camera controllers because they prevent changes to position
        /// </summary>
        private static void ToggleCameraControllers(Transform targetTr, bool enabled)
        {
#if KK || KKS
            foreach (var controllerType in new[] { typeof(Studio.CameraControl), typeof(BaseCameraControl_Ver2), typeof(BaseCameraControl) })
            {
                var cc = targetTr.GetComponent(controllerType);
                if (cc is MonoBehaviour mb)
                    mb.enabled = enabled;
            }

            var actionScene = GameObject.Find("ActionScene/CameraSystem");
            if (actionScene != null) actionScene.GetComponent<CameraSystem>().ShouldUpdate = enabled;
#endif
        }

        #region UI
        protected void OnGUI()
        {
            DrawGuideLines();

            IMGUIUtils.DrawSolidBox(uiRect);
            uiRect = GUILayout.Window(uiWindowHash, uiRect, WindowFunction, "Screenshot settings");
            IMGUIUtils.EatInputInRect(uiRect);
        }

        protected override void DrawGameSpecificUI(GUIStyle titleStyle)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            {
                GUILayout.Label("Card upsampling rate", titleStyle);

                GUILayout.BeginHorizontal();
                {
                    int carddownscale = (int)System.Math.Round(GUILayout.HorizontalSlider(CardDownscalingRate.Value, 1, 4));

                    GUILayout.Label($"{carddownscale}x", new GUIStyle
                    {
                        normal = new GUIStyleState
                        {
                            textColor = Color.white
                        }
                    }, GUILayout.ExpandWidth(false));
                    CardDownscalingRate.Value = carddownscale;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        #endregion

        protected override void PlaySound(ScreenshotSoundType soundType)
        {
            switch (soundType)
            {
                case ScreenshotSoundType.Photo:
                    Utils.Sound.Play(SystemSE.photo);
                    break;
                case ScreenshotSoundType.Custom:
                    // Handle custom sounds if needed
                    break;
            }
        }
    }
}