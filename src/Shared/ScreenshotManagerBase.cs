using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepisPlugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Screencap
{
    [BepInPlugin("GUID", "Name", "1.0")]
    public abstract class ScreenshotManagerBase : BaseUnityPlugin
    {
        /// <summary>
        /// GUID of the plugin, use with BepInDependency
        /// </summary>
        public const string GUID = "com.bepis.bepinex.screenshotmanager";
        /// <summary>
        /// Name of the plugin
        /// </summary>
        public const string PluginName = "Screenshot Manager";
        /// <summary>
        /// Version of the plugin
        /// </summary>
        public const string Version = Metadata.PluginsVersion;
        /// <summary>
        /// Logger of the plugin
        /// </summary>
        internal static new ManualLogSource Logger;

        /// <summary>
        /// Triggered before a screenshot is captured. For use by plugins adding screen effects incompatible with Screencap.
        /// </summary>
        public static event Action OnPreCapture;
        /// <summary>
        /// Triggered after a screenshot is captured. For use by plugins adding screen effects incompatible with Screencap.
        /// </summary>
        public static event Action OnPostCapture;

        /// <summary>
        /// Maximum screenshot size
        /// </summary>
        protected int ScreenshotSizeMax => ResolutionAllowExtreme.Value ? 15360 : 4096;
        /// <summary>
        /// Minimum screenshot size
        /// </summary>
        protected const int ScreenshotSizeMin = 2;

        /// <summary>
        /// Screenshot directory
        /// </summary>
        protected readonly string screenshotDir = Path.Combine(Paths.GameRootPath, @"UserData\cap\");
        /// <summary>
        /// Saved resolutions
        /// </summary>
        protected List<Vector2Int> savedResolutions = new List<Vector2Int>();

        // Common config entries
        protected ConfigEntry<bool> ResolutionAllowExtreme { get; private set; }
        protected ConfigEntry<int> ResolutionX { get; private set; }
        protected ConfigEntry<int> ResolutionY { get; private set; }
        protected ConfigEntry<int> DownscalingRate { get; private set; }
        protected ConfigEntry<bool> ScreenshotMessage { get; private set; }
        protected ConfigEntry<int> UIShotUpscale { get; private set; }
        protected ConfigEntry<KeyboardShortcut> KeyCapture { get; private set; }
        protected ConfigEntry<KeyboardShortcut> KeyCaptureAlpha { get; private set; }
        protected ConfigEntry<KeyboardShortcut> KeyGui { get; private set; }
        protected ConfigEntry<CameraGuideLinesMode> GuideLinesModes { get; private set; }
        protected ConfigEntry<int> GuideLineThickness { get; private set; }
        protected ConfigEntry<string> SavedResolutionsConfig { get; private set; }
        protected ConfigEntry<NameFormat> ScreenshotNameFormat { get; private set; }
        protected ConfigEntry<string> ScreenshotNameOverride { get; private set; }
        protected ConfigEntry<bool> UseJpg { get; private set; }
        protected ConfigEntry<int> JpgQuality { get; private set; }
        protected ConfigEntry<AlphaMode> CaptureAlphaMode { get; private set; }
        protected ConfigEntry<KeyboardShortcut> KeyCapture360 { get; private set; }
        protected ConfigEntry<KeyboardShortcut> KeyCaptureAlphaIn3D { get; private set; }
        protected ConfigEntry<KeyboardShortcut> KeyCapture360in3D { get; private set; }
        protected ConfigEntry<int> Resolution360 { get; private set; }
        protected ConfigEntry<float> EyeSeparation { get; private set; }
        protected ConfigEntry<float> ImageSeparationOffset { get; private set; }
        protected ConfigEntry<bool> FlipEyesIn3DCapture { get; private set; }

        protected readonly int uiWindowHash = GUID.GetHashCode();
        protected readonly Rect uiRect = new Rect(20, Screen.height / 2 - 150, 160, 223);
        protected bool uiShow = false;
        protected string CaptureWidthBuffer = "", CaptureHeightBuffer = "";

        protected virtual KeyCode PrimaryScreenshotKey => KeyCode.F11;
        protected virtual KeyCode SecondaryScreenshotKey => KeyCode.F9;

        protected ScreenshotManagerBase()
        {
            screenshotDir = Path.Combine(Paths.GameRootPath, @"UserData\cap\");
            Logger = base.Logger;
        }

        protected virtual void InitializeSharedSettings()
        {
            KeyCapture360 = Config.Bind(
                "Keyboard shortcuts",
                "Take 360 screenshot",
                new KeyboardShortcut(PrimaryScreenshotKey, KeyCode.LeftControl),
                new ConfigDescription("Captures a 360 screenshot around current camera. The created image is in equirectangular format and can be viewed by most 360 image viewers (e.g. Google Cardboard)."));

            ResolutionAllowExtreme = Config.Bind(
                "Render Settings", "Allow extreme resolutions",
                false,
                new ConfigDescription("Raise maximum rendered screenshot resolution cap to 16k. Trying to take a screenshot too high above 4k WILL CRASH YOUR GAME. ALWAYS SAVE BEFORE ATTEMPTING A SCREENSHOT AND MONITOR RAM USAGE AT ALL TIMES. Changes take effect after restarting the game."));

            ResolutionX = Config.Bind(
                "Render Output Resolution", "Horizontal",
                Screen.width,
                new ConfigDescription("Horizontal size (width) of rendered screenshots in pixels. Doesn't affect UI and 360 screenshots.", new AcceptableValueRange<int>(ScreenshotSizeMin, ScreenshotSizeMax)));

            ResolutionY = Config.Bind(
                "Render Output Resolution", "Vertical",
                Screen.height,
                new ConfigDescription("Vertical size (height) of rendered screenshots in pixels. Doesn't affect UI and 360 screenshots.", new AcceptableValueRange<int>(ScreenshotSizeMin, ScreenshotSizeMax)));

            Resolution360 = Config.Bind(
                "360 Screenshots", "360 screenshot resolution",
                4096,
                new ConfigDescription("Horizontal resolution (width) of 360 degree/panorama screenshots. Decrease if you have issues. WARNING: Memory usage can get VERY high - 4096 needs around 4GB of free RAM/VRAM to create, 8192 will need much more.", new AcceptableValueList<int>(1024, 2048, 4096, 8192)));

            DownscalingRate = Config.Bind(
                "Render Settings", "Screenshot upsampling ratio",
                2,
                new ConfigDescription("Capture screenshots in a higher resolution and then downscale them to desired size. Prevents aliasing, preserves small details and gives a smoother result, but takes longer to create.", new AcceptableValueRange<int>(1, 4)));

            ScreenshotMessage = Config.Bind(
                "General", "Show messages on screen",
                true,
                new ConfigDescription("Whether screenshot messages will be displayed on screen. Messages will still be written to the log."));

            UIShotUpscale = Config.Bind(
                "UI Screenshots", "Screenshot resolution multiplier",
                1,
                new ConfigDescription("Multiplies the UI screenshot resolution from the current game resolution by this amount.\nWarning: Some elements will still be rendered at the original resolution (most notably the interface).", new AcceptableValueRange<int>(1, 8), "Advanced"));

            KeyCapture = Config.Bind(
                "Keyboard shortcuts", "Take UI screenshot",
                new KeyboardShortcut(SecondaryScreenshotKey),
                new ConfigDescription("Capture a simple \"as you see it\" screenshot of the game. Not affected by settings for rendered screenshots."));

            KeyCaptureAlpha = Config.Bind(
                "Keyboard shortcuts", "Take rendered screenshot",
                new KeyboardShortcut(PrimaryScreenshotKey),
                new ConfigDescription("Take a screenshot with no interface. Can be configured by other settings to increase quality and turn on transparency."));

            KeyGui = Config.Bind(
                "Keyboard shortcuts", "Open settings window",
                new KeyboardShortcut(PrimaryScreenshotKey, KeyCode.LeftShift),
                new ConfigDescription("Open a quick access window with the most common settings."));

            GuideLinesModes = Config.Bind(
                "General", "Camera guide lines",
                CameraGuideLinesMode.Framing | CameraGuideLinesMode.GridThirds,
                new ConfigDescription("Draws guide lines on the screen to help with framing rendered screenshots. The guide lines are not captured in the rendered screenshot.\nTo show the guide lines, open the quick access settings window.", null, "Advanced"));

            GuideLineThickness = Config.Bind(
                "General", "Guide lines thickness",
                1,
                new ConfigDescription("Thickness of the guide lines in pixels.", new AcceptableValueRange<int>(1, 5), "Advanced"));

            SavedResolutionsConfig = Config.Bind(
                "Render Settings", "Saved Resolutions",
                string.Empty,
                new ConfigDescription("List of saved resolutions in JSON format.", null, "Debug"));

            ScreenshotNameFormat = Config.Bind(
                "General", "Screenshot filename format",
                NameFormat.NameDateType,
                new ConfigDescription("Screenshots will be saved with names of the selected format. Name stands for the current game name."));

            ScreenshotNameOverride = Config.Bind(
                "General", "Screenshot filename Name override",
                "",
                new ConfigDescription("Forces the Name part of the filename to always be this instead of varying depending on the name of the current game.", null, "Advanced"));

            UseJpg = Config.Bind(
                "JPG Settings", "Save screenshots as .jpg instead of .png",
                false,
                new ConfigDescription("Save screenshots in lower quality in return for smaller file sizes. Transparency is NOT supported in .jpg screenshots. Strongly consider not using this option if you want to share your work."));

            JpgQuality = Config.Bind(
                "JPG Settings", "Quality of .jpg files",
                100,
                new ConfigDescription("Lower quality = lower file sizes. Even 100 is worse than a .png file.", new AcceptableValueRange<int>(1, 100)));

            CaptureAlphaMode = Config.Bind(
                "Render Settings", "Transparency in rendered screenshots",
                AlphaMode.rgAlpha,
                new ConfigDescription("Replaces background with transparency in rendered image. Works only if there are no 3D objects covering the background (e.g. the map)."));

            KeyCaptureAlphaIn3D = Config.Bind(
                "Keyboard shortcuts", "Take rendered 3D screenshot",
                new KeyboardShortcut(PrimaryScreenshotKey, KeyCode.LeftAlt),
                new ConfigDescription("Capture a high quality screenshot without UI in stereoscopic 3D (2 captures for each eye in one image)."));

            KeyCapture360in3D = Config.Bind(
                "Keyboard shortcuts", "Take 360 3D screenshot",
                new KeyboardShortcut(PrimaryScreenshotKey, KeyCode.LeftControl, KeyCode.LeftShift),
                new ConfigDescription("Captures a 360 screenshot around current camera in stereoscopic 3D."));

            EyeSeparation = Config.Bind(
                "3D Settings", "3D screenshot eye separation",
                0.18f,
                new ConfigDescription("Distance between the two captured stereoscopic screenshots.", new AcceptableValueRange<float>(0.01f, 0.5f)));

            ImageSeparationOffset = Config.Bind(
                "3D Settings", "3D screenshot image separation offset",
                0.25f,
                new ConfigDescription("Move images in stereoscopic screenshots closer together by this percentage.", new AcceptableValueRange<float>(0f, 1f)));

            FlipEyesIn3DCapture = Config.Bind(
                "3D Settings", "Flip left and right eye",
                true,
                new ConfigDescription("Flip left and right eye for cross-eyed viewing."));
        }

        protected void LogScreenshotMessage(string text)
        {
            if (ScreenshotMessage.Value)
                Logger.LogMessage(text);
            else
                Logger.LogInfo(text);
        }

        protected void DrawGuideLines()
        {
            var desiredAspect = ResolutionX.Value / (float)ResolutionY.Value;
            var screenAspect = Screen.width / (float)Screen.height;

            if (screenAspect > desiredAspect)
            {
                var actualWidth = Mathf.RoundToInt(Screen.height * desiredAspect);
                var barWidth = Mathf.RoundToInt((Screen.width - actualWidth) / 2f);

                if ((GuideLinesModes.Value & CameraGuideLinesMode.Framing) != 0)
                {
                    IMGUIUtils.DrawTransparentBox(new Rect(0, 0, barWidth, Screen.height));
                    IMGUIUtils.DrawTransparentBox(new Rect(Screen.width - barWidth, 0, barWidth, Screen.height));
                }

                if ((GuideLinesModes.Value & CameraGuideLinesMode.Border) != 0)
                {
                    IMGUIUtils.DrawTransparentBox(new Rect(barWidth, 0, actualWidth, GuideLineThickness.Value));
                    IMGUIUtils.DrawTransparentBox(new Rect(barWidth, Screen.height - GuideLineThickness.Value, actualWidth, GuideLineThickness.Value));
                    IMGUIUtils.DrawTransparentBox(new Rect(barWidth, 0, GuideLineThickness.Value, Screen.height));
                    IMGUIUtils.DrawTransparentBox(new Rect(Screen.width - barWidth - GuideLineThickness.Value, 0, GuideLineThickness.Value, Screen.height));
                }

                if ((GuideLinesModes.Value & CameraGuideLinesMode.GridThirds) != 0)
                    DrawGuides(barWidth, 0, actualWidth, Screen.height, 0.3333333f);

                if ((GuideLinesModes.Value & CameraGuideLinesMode.GridPhi) != 0)
                    DrawGuides(barWidth, 0, actualWidth, Screen.height, 0.236f);
            }
            else
            {
                var actualHeight = Mathf.RoundToInt(Screen.width / desiredAspect);
                var barHeight = Mathf.RoundToInt((Screen.height - actualHeight) / 2f);

                if ((GuideLinesModes.Value & CameraGuideLinesMode.Framing) != 0)
                {
                    IMGUIUtils.DrawTransparentBox(new Rect(0, 0, Screen.width, barHeight));
                    IMGUIUtils.DrawTransparentBox(new Rect(0, Screen.height - barHeight, Screen.width, barHeight));
                }

                if ((GuideLinesModes.Value & CameraGuideLinesMode.Border) != 0)
                {
                    IMGUIUtils.DrawTransparentBox(new Rect(0, barHeight, Screen.width, GuideLineThickness.Value));
                    IMGUIUtils.DrawTransparentBox(new Rect(0, Screen.height - barHeight - GuideLineThickness.Value, Screen.width, GuideLineThickness.Value));
                    IMGUIUtils.DrawTransparentBox(new Rect(0, barHeight, GuideLineThickness.Value, actualHeight));
                    IMGUIUtils.DrawTransparentBox(new Rect(Screen.width - GuideLineThickness.Value, barHeight, GuideLineThickness.Value, actualHeight));
                }

                if ((GuideLinesModes.Value & CameraGuideLinesMode.GridThirds) != 0)
                    DrawGuides(0, barHeight, Screen.width, actualHeight, 0.3333333f);

                if ((GuideLinesModes.Value & CameraGuideLinesMode.GridPhi) != 0)
                    DrawGuides(0, barHeight, Screen.width, actualHeight, 0.236f);
            }
        }

        private void DrawGuides(int offsetX, int offsetY, int viewportWidth, int viewportHeight, float centerRatio)
        {
            var sideRatio = (1 - centerRatio) / 2;
            var secondRatio = sideRatio + centerRatio;

            var firstx = offsetX + viewportWidth * sideRatio;
            var secondx = offsetX + viewportWidth * secondRatio;
            IMGUIUtils.DrawTransparentBox(new Rect(Mathf.RoundToInt(firstx), offsetY, GuideLineThickness.Value, viewportHeight));
            IMGUIUtils.DrawTransparentBox(new Rect(Mathf.RoundToInt(secondx), offsetY, GuideLineThickness.Value, viewportHeight));

            var firsty = offsetY + viewportHeight * sideRatio;
            var secondy = offsetY + viewportHeight * secondRatio;
            IMGUIUtils.DrawTransparentBox(new Rect(offsetX, Mathf.RoundToInt(firsty), viewportWidth, GuideLineThickness.Value));
            IMGUIUtils.DrawTransparentBox(new Rect(offsetX, Mathf.RoundToInt(secondy), viewportWidth, GuideLineThickness.Value));
        }

        protected string GetUniqueFilename(string capType)
        {
            string filename;

            var productName = Application.productName.Replace(" ", "");
            if (!string.IsNullOrEmpty(ScreenshotNameOverride.Value))
                productName = ScreenshotNameOverride.Value;

            var extension = UseJpg.Value ? "jpg" : "png";

            switch (ScreenshotNameFormat.Value)
            {
                case NameFormat.NameDate:
                    filename = $"{productName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.{extension}";
                    break;
                case NameFormat.NameTypeDate:
                    filename = $"{productName}-{capType}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.{extension}";
                    break;
                case NameFormat.NameDateType:
                    filename = $"{productName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}-{capType}.{extension}";
                    break;
                case NameFormat.TypeDate:
                    filename = $"{capType}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.{extension}";
                    break;
                case NameFormat.TypeNameDate:
                    filename = $"{capType}-{productName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.{extension}";
                    break;
                case NameFormat.Date:
                    filename = $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.{extension}";
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Unhandled screenshot filename format - " + ScreenshotNameFormat.Value);
            }

            return Path.GetFullPath(Path.Combine(screenshotDir, filename));
        }

        protected void LoadSavedResolutions()
        {
            if (!string.IsNullOrEmpty(SavedResolutionsConfig.Value))
            {
                savedResolutions = new List<Vector2Int>();
                var matches = System.Text.RegularExpressions.Regex.Matches(SavedResolutionsConfig.Value, @"\((\-?\d+),(\-?\d+)\)");
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    int x = int.Parse(match.Groups[1].Value);
                    int y = int.Parse(match.Groups[2].Value);
                    savedResolutions.Add(new Vector2Int(x, y));
                }
            }
        }

        protected void SaveSavedResolutions()
        {
            SavedResolutionsConfig.Value = "[" + string.Join(", ", savedResolutions.Select(v => $"({v.x},{v.y})")) + "]";
        }

        protected void SaveCurrentResolution()
        {
            var resolution = new Vector2Int(ResolutionX.Value, ResolutionY.Value);
            if (!savedResolutions.Contains(resolution))
            {
                savedResolutions.Add(resolution);
                SaveSavedResolutions();
            }
        }

        protected void DeleteResolution(Vector2Int resolution)
        {
            savedResolutions.Remove(resolution);
            SaveSavedResolutions();
        }

        [Flags]
        public enum CameraGuideLinesMode
        {
            [Description("No guide lines")]
            None = 0,
            [Description("Cropped area")]
            Framing = 1 << 0,
            [Description("Rule of thirds")]
            GridThirds = 1 << 1,
            [Description("Golden ratio")]
            GridPhi = 1 << 2,
            [Description("Grid border")]
            Border = 1 << 3
        }

        public enum NameFormat
        {
            [Description("GameName-2024-03-14-12-34-56")]
            NameDate,
            [Description("GameName-Type-2024-03-14-12-34-56")]
            NameTypeDate,
            [Description("GameName-2024-03-14-12-34-56-Type")]
            NameDateType,
            [Description("Type-2024-03-14-12-34-56")]
            TypeDate,
            [Description("Type-GameName-2024-03-14-12-34-56")]
            TypeNameDate,
            [Description("2024-03-14-12-34-56")]
            Date
        }

        public enum AlphaMode
        {
            [Description("No transparency")]
            None,
            [Description("Cutout transparency")]
            blackout,
            [Description("Full transparency")]
            rgAlpha
        }

        protected enum ScreenshotSoundType
        {
            Photo,
            Custom
        }

        protected abstract void PlaySound(ScreenshotSoundType soundType);

        protected void PlayCaptureSound()
        {
            PlaySound(ScreenshotSoundType.Photo);
        }

        // Abstract methods that must be implemented by derived classes
        protected abstract void CaptureScreenshotNormal();
        protected abstract void CaptureScreenshotRender();
        protected abstract void InitializeSettings();

        protected virtual void Update()
        {
            if (KeyGui.Value.IsDown())
            {
                uiShow = !uiShow;
                CaptureWidthBuffer = ResolutionX.Value.ToString();
                CaptureHeightBuffer = ResolutionY.Value.ToString();
            }
            else if (KeyCapture.Value.IsDown())
            {
                CaptureScreenshotNormal();
            }
            else if (KeyCaptureAlpha.Value.IsDown())
            {
                CaptureScreenshotRender();
            }
            else if (KeyCapture360.Value.IsDown())
            {
                StartCoroutine(Take360Screenshot(false));
            }
            else if (KeyCaptureAlphaIn3D.Value.IsDown())
            {
                StartCoroutine(TakeCharScreenshot(true));
            }
            else if (KeyCapture360in3D.Value.IsDown())
            {
                StartCoroutine(Take360Screenshot(true));
            }
        }

        protected IEnumerator WaitForEndOfFrameThen(Action action)
        {
            yield return new WaitForEndOfFrame();
            action();
        }

        protected virtual byte[] EncodeToFile(Texture2D result) => UseJpg.Value ? result.EncodeToJPG(JpgQuality.Value) : result.EncodeToPNG();

        protected virtual byte[] EncodeToXmpFile(Texture2D result) => UseJpg.Value ? I360Render.InsertXMPIntoTexture2D_JPEG(result, JpgQuality.Value) : I360Render.InsertXMPIntoTexture2D_PNG(result);

        protected void OnGUI()
        {
            if (uiShow)
            {
                DrawGuideLines();

                IMGUIUtils.DrawSolidBox(uiRect);
                uiRect = GUILayout.Window(uiWindowHash, uiRect, WindowFunction, "Screenshot settings");
                IMGUIUtils.EatInputInRect(uiRect);
            }
        }

        protected virtual void WindowFunction(int windowID)
        {
            var titleStyle = new GUIStyle
            {
                normal = new GUIStyleState
                {
                    textColor = Color.white
                }
            };

            // Resolution settings
            DrawResolutionSettings(titleStyle);

            // Saved resolutions
            DrawSavedResolutions(titleStyle);

            // Upsampling settings
            DrawUpsamplingSettings(titleStyle);

            // Transparency settings
            DrawTransparencySettings(titleStyle);

            // Guide lines settings
            DrawGuideLineSettings(titleStyle);

            // Screenshot buttons
            DrawScreenshotButtons();

            // Allow derived classes to add their own UI elements
            DrawGameSpecificUI(titleStyle);

            GUI.DragWindow();
        }

        protected virtual void DrawGameSpecificUI(GUIStyle titleStyle)
        {
            // Override in derived classes to add game-specific UI elements
        }

        private void DrawResolutionSettings(GUIStyle titleStyle)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            {
                GUILayout.Label("Output resolution (W/H)", titleStyle);

                GUILayout.BeginHorizontal();
                {
                    GUI.SetNextControlName("X");
                    CaptureWidthBuffer = GUILayout.TextField(CaptureWidthBuffer);

                    GUI.SetNextControlName("Y");
                    CaptureHeightBuffer = GUILayout.TextField(CaptureHeightBuffer);

                    var focused = GUI.GetNameOfFocusedControl();
                    if (focused != "X" && focused != "Y" || Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                    {
                        if (!int.TryParse(CaptureWidthBuffer, out int x))
                            x = ResolutionX.Value;
                        if (!int.TryParse(CaptureHeightBuffer, out int y))
                            y = ResolutionY.Value;
                        CaptureWidthBuffer = (ResolutionX.Value = Mathf.Clamp(x, ScreenshotSizeMin, ScreenshotSizeMax)).ToString();
                        CaptureHeightBuffer = (ResolutionY.Value = Mathf.Clamp(y, ScreenshotSizeMin, ScreenshotSizeMax)).ToString();
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("1:1"))
                    {
                        var max = Mathf.Max(ResolutionX.Value, ResolutionY.Value);
                        ResolutionX.Value = max;
                        ResolutionY.Value = max;
                    }
                    if (GUILayout.Button("4:3"))
                    {
                        var max = Mathf.Max(ResolutionX.Value, ResolutionY.Value);
                        ResolutionX.Value = max;
                        ResolutionY.Value = Mathf.RoundToInt(max * (3f / 4f));
                    }
                    if (GUILayout.Button("16:9"))
                    {
                        var max = Mathf.Max(ResolutionX.Value, ResolutionY.Value);
                        ResolutionX.Value = max;
                        ResolutionY.Value = Mathf.RoundToInt(max * (9f / 16f));
                    }
                    if (GUILayout.Button("6:10"))
                    {
                        var max = Mathf.Max(ResolutionX.Value, ResolutionY.Value);
                        ResolutionX.Value = Mathf.RoundToInt(max * (6f / 10f));
                        ResolutionY.Value = max;
                    }
                }
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Set to screen size"))
                {
                    ResolutionX.Value = Screen.width;
                    ResolutionY.Value = Screen.height;
                }

                if (GUILayout.Button("Rotate 90 degrees"))
                {
                    var currentX = ResolutionX.Value;
                    ResolutionX.Value = ResolutionY.Value;
                    ResolutionY.Value = currentX;
                }

                if (GUILayout.Button("Save current resolution"))
                {
                    SaveCurrentResolution();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawSavedResolutions(GUIStyle titleStyle)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            {
                GUILayout.Label("Saved Resolutions", titleStyle);
                foreach (var resolution in savedResolutions.ToList())
                {
                    GUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button($"{resolution.x}x{resolution.y}"))
                        {
                            ResolutionX.Value = resolution.x;
                            ResolutionY.Value = resolution.y;
                        }
                        if (GUILayout.Button("X", GUILayout.ExpandWidth(false)))
                        {
                            DeleteResolution(resolution);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawUpsamplingSettings(GUIStyle titleStyle)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            {
                GUILayout.Label("Screen upsampling rate", titleStyle);

                GUILayout.BeginHorizontal();
                {
                    int downscale = (int)System.Math.Round(GUILayout.HorizontalSlider(DownscalingRate.Value, 1, 4));

                    GUILayout.Label($"{downscale}x", new GUIStyle
                    {
                        normal = new GUIStyleState
                        {
                            textColor = Color.white
                        }
                    }, GUILayout.ExpandWidth(false));
                    DownscalingRate.Value = downscale;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private void DrawTransparencySettings(GUIStyle titleStyle)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            {
                GUILayout.Label("Transparency mode", titleStyle);
                GUILayout.BeginHorizontal();
                {
                    GUI.changed = false;
                    var val = GUILayout.Toggle(CaptureAlphaMode.Value == AlphaMode.None, "None");
                    if (GUI.changed && val) CaptureAlphaMode.Value = AlphaMode.None;

                    GUI.changed = false;
                    val = GUILayout.Toggle(CaptureAlphaMode.Value == AlphaMode.blackout, "Cutout");
                    if (GUI.changed && val) CaptureAlphaMode.Value = AlphaMode.blackout;

                    GUI.changed = false;
                    val = GUILayout.Toggle(CaptureAlphaMode.Value == AlphaMode.rgAlpha, "Full");
                    if (GUI.changed && val) CaptureAlphaMode.Value = AlphaMode.rgAlpha;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private void DrawGuideLineSettings(GUIStyle titleStyle)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            {
                GUILayout.Label("Guide lines", titleStyle);
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Thickness", GUILayout.ExpandWidth(false));
                    GUILayout.Space(2);
                    GuideLineThickness.Value = (int)System.Math.Round(GUILayout.HorizontalSlider(GuideLineThickness.Value, 1, 5));
                    GUILayout.Label($"{GuideLineThickness.Value}px", new GUIStyle
                    {
                        normal = new GUIStyleState
                        {
                            textColor = Color.white
                        }
                    }, GUILayout.ExpandWidth(false));
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                {
                    GUI.changed = false;
                    var val = GUILayout.Toggle((GuideLinesModes.Value & CameraGuideLinesMode.Framing) != 0, "Frame");
                    if (GUI.changed) GuideLinesModes.Value = val ? GuideLinesModes.Value | CameraGuideLinesMode.Framing : GuideLinesModes.Value & ~CameraGuideLinesMode.Framing;

                    GUI.changed = false;
                    val = GUILayout.Toggle((GuideLinesModes.Value & CameraGuideLinesMode.Border) != 0, "Border");
                    if (GUI.changed) GuideLinesModes.Value = val ? GuideLinesModes.Value | CameraGuideLinesMode.Border : GuideLinesModes.Value & ~CameraGuideLinesMode.Border;

                    GUI.changed = false;
                    val = GUILayout.Toggle((GuideLinesModes.Value & CameraGuideLinesMode.GridThirds) != 0, "3rds");
                    if (GUI.changed) GuideLinesModes.Value = val ? GuideLinesModes.Value | CameraGuideLinesMode.GridThirds : GuideLinesModes.Value & ~CameraGuideLinesMode.GridThirds;

                    GUI.changed = false;
                    val = GUILayout.Toggle((GuideLinesModes.Value & CameraGuideLinesMode.GridPhi) != 0, "Phi");
                    if (GUI.changed) GuideLinesModes.Value = val ? GuideLinesModes.Value | CameraGuideLinesMode.GridPhi : GuideLinesModes.Value & ~CameraGuideLinesMode.GridPhi;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        protected virtual void DrawScreenshotButtons()
        {
            if (GUILayout.Button("Open screenshot dir"))
                Process.Start(screenshotDir);

            GUILayout.Space(10);
            if (GUILayout.Button($"Capture Normal ({SecondaryScreenshotKey})"))
                CaptureScreenshotNormal();
            if (GUILayout.Button($"Capture Render ({PrimaryScreenshotKey})"))
                CaptureScreenshotRender();
            if (GUILayout.Button($"Capture 360 ({PrimaryScreenshotKey} + Ctrl)"))
                StartCoroutine(Take360Screenshot(false));
            if (GUILayout.Button($"Capture 3D ({PrimaryScreenshotKey} + Alt)"))
                StartCoroutine(TakeCharScreenshot(true));
            if (GUILayout.Button($"Capture 360 3D ({PrimaryScreenshotKey} + Ctrl + Shift)"))
                StartCoroutine(Take360Screenshot(true));

            GUILayout.Space(2);
            GUILayout.Label("More in Plugin Settings");
        }

        protected IEnumerator TakeScreenshotLog(string filename)
        {
            yield return new WaitForEndOfFrame();
            PlayCaptureSound();
            LogScreenshotMessage($"Screenshot saved to {filename.Substring(Paths.GameRootPath.Length)}");
        }

        protected virtual void CaptureScreenshotNormal()
        {
            PlayCaptureSound();
            var path = GetUniqueFilename("UI");
            ScreenCapture.CaptureScreenshot(path, UIShotUpscale.Value);
            StartCoroutine(TakeScreenshotLog(path));
        }

        protected virtual void OnPreCapture()
        {
            try { OnPreCapture?.Invoke(); }
            catch (Exception ex) { Logger.LogError(ex); }
        }

        protected virtual void OnPostCapture()
        {
            try { OnPostCapture?.Invoke(); }
            catch (Exception ex) { Logger.LogError(ex); }
        }

        protected virtual void StitchImages(Texture2D capture, Texture2D capture2, float overlapOffset)
        {
            var xAdjust = (int)(capture.width * overlapOffset);
            var result = new Texture2D((capture.width - xAdjust) * 2, capture.height, TextureFormat.ARGB32, false);

            int width = result.width / 2;
            int height = result.height;
            result.SetPixels(0, 0, width, height, capture.GetPixels(0, 0, width, height));
            result.SetPixels(width, 0, width, height, capture2.GetPixels(xAdjust, 0, width, height));

            result.Apply();
            return result;
        }

    }
} 