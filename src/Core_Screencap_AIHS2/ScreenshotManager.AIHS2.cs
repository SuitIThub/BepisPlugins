using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepisPlugins;
using Pngcs.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;

namespace Screencap
{
    /// <summary>
    /// Brought to AI-Shoujo by essu - the local smug, benevolent modder.
    /// Tool Window ported from KK by SuitIThub
    /// </summary>
    public partial class ScreenshotManager
    {
        #region Config properties

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        public static ConfigEntry<int> CustomShadowResolution { get; set; }
        public static ConfigEntry<ShadowCascades> ShadowCascadeOverride { get; set; }
        public static ConfigEntry<DisableAOSetting> DisableAO { get; set; }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

        private void InitializeGameSpecific()
        {
            CustomShadowResolution = Config.Bind(
                "Render Settings", "Shadow resolution override",
                8192,
                new ConfigDescription("By default, shadow map resolution is computed from its importance on screen. Setting this to a value greater than zero will override that behavior. Please note that the shadow map resolution will still be capped by memory and hardware limits.", new AcceptableValueList<int>(0, 4096, 8192, 16384, 32768)));

            ShadowCascadeOverride = Config.Bind(
                "Render Settings", "Shadow cascade override",
                ShadowCascades.Four,
                new ConfigDescription("When capturing screenshots, different shadow cascade values may look better. Override it or keep the current value."));

            DisableAO = Config.Bind(
                "Render Settings", "Disable AO",
                DisableAOSetting.WhenUpsampling,
                new ConfigDescription("When capturing screenshots, upsampling can cause ambient occlusion to start banding and produce weird effects on the end image. Change this setting to disable AO when capturing the screenshot."));
        }

        /// <summary>
        /// Specifies the number of shadow cascades to use for rendering. 
        /// When capturing screenshots, different shadow cascade values may look better.
        /// </summary>
        public enum ShadowCascades
        {
            /// <summary> Keep the current value. </summary>
            [System.ComponentModel.Description("Keep current value")]
            Off = -1,
            /// <summary> Force zero shadow cascades (turn them off). </summary>
            Zero = 0,
            /// <summary> Force two shadow cascades. </summary>
            Two = 2,
            /// <summary> Force four shadow cascades. </summary>
            Four = 4,
        }

        /// <summary>  
        /// Specifies the behavior for disabling Ambient Occlusion (AO) during screenshot capture.  
        /// </summary>  
        public enum DisableAOSetting
        {
            /// <summary> Always disable Ambient Occlusion regardless of other settings. </summary>  
            Always,
            /// <summary> Disable Ambient Occlusion only when upsampling is enabled to prevent artifacts. </summary>  
            WhenUpsampling,
            /// <summary> Keep the original game settings. </summary>  
            Never
        }

        #endregion

        #region Screenshot Handler

        private IEnumerator TakeRenderScreenshot(bool in3D)
        {
            FirePreCapture();

            var filename = GetUniqueFilename(in3D ? "3D-Render" : "Render", UseJpg.Value);
            LogScreenshotMessage(in3D ? "3D rendered" : "rendered", filename);
            PlayCaptureSound();

            var sc = QualitySettings.shadowCascades;

            if (ShadowCascadeOverride.Value != ShadowCascades.Off)
                QualitySettings.shadowCascades = (int)ShadowCascadeOverride.Value;

            var lights = FindObjectsOfType<Light>();
            foreach (var l in lights)
                l.shadowCustomResolution = CustomShadowResolution.Value;

            yield return new WaitForEndOfFrame();

            var alphaAllowed = SceneManager.GetActiveScene().name == "CharaCustom" || Constants.InsideStudio;
            var alpha = CaptureAlphaMode.Value != AlphaMode.None && alphaAllowed ? AlphaModeUtils.Default : AlphaMode.None;

            var output = !in3D ? CaptureRender(transparencyMode: alpha) : Do3DCapture(() => CaptureRender(transparencyMode: alpha));

            QualitySettings.shadowCascades = sc;

            foreach (var l in lights)
                l.shadowCustomResolution = 0;

            FirePostCapture();

            if (output != null)
                yield return WriteTex(output, alpha, filename);
        }

        private RenderTexture DoCaptureRender(int width, int height, int downscaling, AlphaMode transparencyMode)
        {
            return transparencyMode == AlphaMode.None ? CaptureOpaque(width, height, downscaling) : CaptureTransparent(width, height, downscaling);
        }

        /// <summary>
        /// Captures an opaque screenshot at specified resolution with optional upsampling.
        /// Handles depth of field adjustments for the capture.
        /// </summary>
        private static RenderTexture CaptureOpaque(int width, int height, int downscaling)
        {
            var scaledWidth = width * downscaling;
            var scaledHeight = height * downscaling;

            var cam = Camera.main.gameObject;
            var dof = cam.GetComponent<UnityStandardAssets.ImageEffects.DepthOfField>();
            float dofPrevBlurSize = 0;
            if (dof != null)
            {
                dofPrevBlurSize = dof.maxBlurSize;
                // Scale blur size proportionally with resolution to maintain consistent DoF effect
                // Higher resolution needs proportionally larger blur radius
                var ratio = Screen.height / (float)scaledHeight;
                dof.maxBlurSize *= ratio * downscaling;
            }

            var colour = CaptureScreen(scaledWidth, scaledHeight, false);

            if (downscaling > 1)
                colour = ScaleTex(colour, width, height, downscaling);

            if (dof != null)
            {
                dof.maxBlurSize = dofPrevBlurSize;
            }

            return colour;
        }

        /// <summary>
        /// Captures a transparent screenshot by disabling background and compositing alpha.
        /// Uses red/green two-pass when available so semi-transparent areas (e.g. skirt) are preserved.
        /// </summary>
        private static RenderTexture CaptureTransparent(int width, int height, int downscaling)
        {
            var scaledWidth = width * downscaling;
            var scaledHeight = height * downscaling;

            var cam = Camera.main.gameObject;
            var dof = cam.GetComponent<UnityStandardAssets.ImageEffects.DepthOfField>();
            float dofPrevBlurSize = 0;
            if (dof != null)
            {
                dofPrevBlurSize = dof.maxBlurSize;
                var ratio = Screen.height / (float)scaledHeight; //Use larger of width/height?
                dof.maxBlurSize *= ratio * downscaling;
            }

            var colour = CaptureScreen(scaledWidth, scaledHeight, false);

            var ppl = cam.GetComponent<PostProcessLayer>();
            if (ppl != null) ppl.enabled = false;

            // Disable background so we only render the character/scene objects.
            var bg = SceneManager.GetActiveScene().GetRootGameObjects()[0].transform.Find("CustomControl/Map3D/p_ai_mi_createBG00_00");
            GameObject m3D = null;
            if (bg != null) m3D = bg.gameObject;

            if (m3D != null) m3D.SetActive(false);

            if (dof != null)
            {
                dof.maxBlurSize = dofPrevBlurSize;
                if (dof.enabled) dof.enabled = false;
                else dof = null;
            }

            // Ensure optional two-pass bundles are loaded so we can prefer red/green alpha.
            if (!_matRgAlpha) LoadBundleRgAlpha();
            if (!_matMask) LoadBundleBlackout();

            var useTwoPass = _rgAlphaAvailable && _matMask != null && _matMask.shader != null;
            if (useTwoPass && !_twoPassLogged)
            {
                _twoPassLogged = true;
                Logger.LogInfo("Screencap: using red/green two-pass alpha capture (semi-transparent areas preserved).");
            }

            RenderTexture result;
            if (useTwoPass)
            {
                // Two-pass red/green: semi-transparent pixels get correct alpha instead of being dropped.
                var rtR = CaptureScreenWithBackground(scaledWidth, scaledHeight, new Color(1, 0, 0, 1));
                var rtG = CaptureScreenWithBackground(scaledWidth, scaledHeight, new Color(0, 1, 0, 1));

                var rtAlphaMask = RenderTexture.GetTemporary(scaledWidth, scaledHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                MatRgAlpha.SetTexture("_green", rtG);
                Graphics.Blit(rtR, rtAlphaMask, MatRgAlpha);

                result = RenderTexture.GetTemporary(scaledWidth, scaledHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(Texture2D.blackTexture, result);
                MatMask.SetTexture("_MainTex", colour);
                MatMask.SetTexture("_Mask", rtAlphaMask);
                Graphics.Blit(colour, result, MatMask);

                RenderTexture.ReleaseTemporary(rtR);
                RenderTexture.ReleaseTemporary(rtG);
                RenderTexture.ReleaseTemporary(rtAlphaMask);
                RenderTexture.ReleaseTemporary(colour);
            }
            else
            {
                // Fallback: single transparent pass (semi-transparent areas may be lost).
                var mask = CaptureScreen(scaledWidth, scaledHeight, true);
                result = RenderTexture.GetTemporary(scaledWidth, scaledHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(Texture2D.blackTexture, result);
                MatComposite.SetTexture("_Overlay", mask);
                Graphics.Blit(colour, result, MatComposite);
                RenderTexture.ReleaseTemporary(mask);
                RenderTexture.ReleaseTemporary(colour);
            }

            if (ppl != null) ppl.enabled = true;
            if (dof != null) dof.enabled = true;
            if (m3D != null) m3D.SetActive(true);

            if (downscaling > 1)
                result = ScaleTex(result, width, height, downscaling);

            return result;
        }

        /// <summary>
        /// Renders the scene with a solid background color. Used for red/green alpha derivation
        /// so semi-transparent areas blend with the background and can be recovered.
        /// </summary>
        private static RenderTexture CaptureScreenWithBackground(int width, int height, Color backgroundColor)
        {
            var aos = DisableAmbientOcclusion();
            var rt = RenderTexture.GetTemporary(width, height, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var cam = Camera.main;
            var oldCf = cam.clearFlags;
            var oldBg = cam.backgroundColor;
            var oldRt = cam.targetTexture;
            var oldRtc = Camera.current.targetTexture;

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            cam.targetTexture = rt;
            cam.Render();

            cam.clearFlags = oldCf;
            cam.backgroundColor = oldBg;
            cam.targetTexture = oldRt;
            Camera.current.targetTexture = oldRtc;

            if (DisableAO.Value == DisableAOSetting.Always || DisableAO.Value == DisableAOSetting.WhenUpsampling && DownscalingRate.Value > 1)
                foreach (var ao in aos)
                    ao.enabled.value = true;

            return rt;
        }

        #endregion

        #region Image Processing

        private static Material _matComposite;
        private static Material _matScale;
        private static Material _matRgAlpha;
        private static Material _matMask;
        private static bool _rgAlphaAvailable;
        private static bool _twoPassLogged;

        private static Material MatComposite
        {
            get
            {
                if (!_matComposite) LoadBundleComposite();
                return _matComposite;
            }
        }
        private static Material MatScale
        {
            get
            {
                if (!_matScale) LoadBundleComposite();
                return _matScale;
            }
        }
        private static Material MatRgAlpha
        {
            get
            {
                if (!_matRgAlpha) LoadBundleRgAlpha();
                return _matRgAlpha;
            }
        }
        private static Material MatMask
        {
            get
            {
                if (!_matMask) LoadBundleBlackout();
                return _matMask;
            }
        }

        /// <summary>
        /// Load embedded resource bytes when multiple manifest names can match (e.g. Screencap.rgalpha.unity3d and Screencap.Resources.rgalpha.unity3d).
        /// Prefers the name containing ".Resources." so the explicit embed from Core_Screencap_AIHS2/Resources is used.
        /// </summary>
        private static byte[] GetEmbeddedResourceBytes(Assembly asm, string fileName)
        {
            var names = asm.GetManifestResourceNames().Where(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (names.Count == 0) return null;
            var name = names.FirstOrDefault(n => n.Contains(".Resources.")) ?? names[0];
            using (var stream = asm.GetManifestResourceStream(name))
                return stream != null ? ResourceUtils.ReadAllBytes(stream) : null;
        }

        private static void LoadBundleRgAlpha()
        {
            var asm = Assembly.GetExecutingAssembly();
            var bytes = GetEmbeddedResourceBytes(asm, "rgalpha.unity3d") ?? GetEmbeddedResourceBytes(Assembly.GetEntryAssembly(), "rgalpha.unity3d");
            if (bytes == null || bytes.Length == 0) { _rgAlphaAvailable = false; return; }
            try
            {
                var ab = AssetBundle.LoadFromMemory(bytes);
                var shader = ab.LoadAsset<Shader>("rgAlpha2") ?? ab.LoadAsset<Shader>("rgAlpha");
                _matRgAlpha = shader != null ? new Material(shader) : null;
                ab.Unload(false);
                _rgAlphaAvailable = _matRgAlpha != null && _matRgAlpha.shader != null;
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Screencap: failed to load rgalpha bundle: {e.Message}");
                _rgAlphaAvailable = false;
            }
        }

        private static void LoadBundleBlackout()
        {
            var asm = Assembly.GetExecutingAssembly();
            var bytes = GetEmbeddedResourceBytes(asm, "blackout.unity3d") ?? GetEmbeddedResourceBytes(Assembly.GetEntryAssembly(), "blackout.unity3d");
            if (bytes == null || bytes.Length == 0) return;
            try
            {
                var ab = AssetBundle.LoadFromMemory(bytes);
                var shader = ab.LoadAsset<Shader>("alphamask.shader") ?? ab.LoadAsset<Shader>("alphaMask") ?? ab.LoadAsset<Shader>("Shader Forge/alphaMask");
                _matMask = shader != null ? new Material(shader) : null;
                ab.Unload(false);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Screencap: failed to load blackout bundle: {e.Message}");
            }
        }

        /// <summary>
        /// Scales a render texture to the target resolution using custom shader.
        /// Used for downscaling high resolution captures to final output size.
        /// </summary>
        private static RenderTexture ScaleTex(Texture input, int width, int height, int downScaling)
        {
            var resized = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            // Pack downscaling parameters into a Vector4:
            // xy: downscaling factors for width and height
            // zw: final target dimensions
            // This format is required by the resize shader
            MatScale.SetVector("_KernelAndSize", new Vector4(downScaling, downScaling, width, height));
            Graphics.Blit(input, resized, MatScale);

            if (input is RenderTexture rtInput)
                RenderTexture.ReleaseTemporary(rtInput);
            else
                Destroy(input);

            return resized;    // Give em the ol' switcheroo
        }

        private static void LoadBundleComposite()
        {
            var ab = AssetBundle.LoadFromMemory(ResourceUtils.GetEmbeddedResource("composite.unity3d"));
            _matComposite = new Material(ab.LoadAsset<Shader>("composite"));
            _matScale = new Material(ab.LoadAsset<Shader>("resize"));
            ab.Unload(false);
        }

        private static IEnumerable<AmbientOcclusion> DisableAmbientOcclusion()
        {
            var aos = new List<AmbientOcclusion>();

            // Disable ambient occlusion based on settings:
            // - Always: Disable regardless of other settings
            // - WhenUpsampling: Only disable when downscaling > 1 to prevent artifacts
            // - Never: Keep AO enabled
            // Returns list of disabled AO components to re-enable later
            if (DisableAO.Value == DisableAOSetting.Always || DisableAO.Value == DisableAOSetting.WhenUpsampling && DownscalingRate.Value > 1)
                foreach (var vol in FindObjectsOfType<PostProcessVolume>())
                {
                    if (vol.profile.TryGetSettings(out AmbientOcclusion ao))
                    {
                        if (!ao.enabled.value) continue;
                        ao.enabled.value = false;
                        aos.Add(ao);
                    }
                }

            return aos;
        }

        /// <summary>
        /// Writes the captured RenderTexture to a PNG file asynchronously.
        /// Handles both RGBA32 (transparent) and RGBAFloat (opaque) formats.
        /// </summary>
        private static IEnumerator WriteTex(RenderTexture rt, AlphaMode alpha, string filename)
        {
            if (UseJpg.Value)
            {
                // TODO Not async
                var t2d = rt.CopyToTexture2D();
                RenderTexture.ReleaseTemporary(rt);
                yield return null;
                var encoded = t2d.EncodeToJPG(JpgQuality.Value);
                GameObject.DestroyImmediate(t2d);
                yield return null;
                File.WriteAllBytes(filename, encoded);
            }
            else
            {
                // Pull texture off of GPU
                // Not available on KK/EC/KKS, possibly achievable with https://github.com/SlightlyMad/AsyncTextureReader instead
                var req = AsyncGPUReadback.Request(rt, 0, 0, rt.width, 0, rt.height, 0, 1, alpha != AlphaMode.None ? TextureFormat.RGBA32 : TextureFormat.RGBAFloat);
                while (!req.done) yield return null;

                RenderTexture.ReleaseTemporary(rt);

                //Write raw pixel data to a file
                //Uses pngcs Unity fork: https://github.com/andrew-raphael-lukasik/pngcs
                if (alpha != AlphaMode.None)
                {
                    using (var buffer = req.GetData<Color32>())
                        yield return PNG.WriteAsync(buffer.ToArray(), req.width, req.height, 8, true, false, filename);
                }
                else
                {
                    using (var buffer = req.GetData<Color>())
                        yield return PNG.WriteAsync(buffer.ToArray(), req.width, req.height, 8, false, false, filename);
                }
            }
        }

        private static RenderTexture CaptureScreen(int width, int height, bool alpha)
        {
            // Temporarily disable ambient occlusion to prevent artifacts
            var aos = DisableAmbientOcclusion();

            // Select appropriate render texture format:
            // - ARGB32 for transparent captures (alpha channel needed)
            // - Default for opaque captures (better color precision)
            var fmt = alpha ? RenderTextureFormat.ARGB32 : RenderTextureFormat.Default;
            var rt = RenderTexture.GetTemporary(width, height, 32, fmt, RenderTextureReadWrite.Default);

            var cam = Camera.main;

            // Store original camera settings to restore later
            var oldCf = cam.clearFlags;
            var oldBg = cam.backgroundColor;
            var oldRt = cam.targetTexture;
            var oldRtc = Camera.current.targetTexture;

            // Configure camera for capture:
            // - For transparent captures: Use solid color clear and transparent background
            // - For opaque captures: Keep original settings
            cam.clearFlags = alpha ? CameraClearFlags.SolidColor : oldCf;
            cam.backgroundColor = alpha ? new Color(0, 0, 0, 0) : oldBg;
            cam.targetTexture = rt;

            cam.Render();

            cam.clearFlags = oldCf;
            cam.backgroundColor = oldBg;
            cam.targetTexture = oldRt;
            Camera.current.targetTexture = oldRtc;

            // Restore postprocessing settings
            if (DisableAO.Value == DisableAOSetting.Always || DisableAO.Value == DisableAOSetting.WhenUpsampling && DownscalingRate.Value > 1)
                foreach (var ao in aos)
                    ao.enabled.value = true;

            return rt;
        }

        #endregion
    }
}