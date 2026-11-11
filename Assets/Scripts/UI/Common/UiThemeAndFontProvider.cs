using TMPro;
using UnityEngine;

namespace CapsuleArena.UI.Common
{
    /// <summary>
    /// Shared zero-allocation UI helper supplying a guaranteed fallback <see cref="TMP_FontAsset"/>,
    /// crisp procedural sprites for uGUI radial/horizontal fills, and pre-cached 0.1s cooldown strings
    /// (<c>"0.1"</c>..<c>"9.9"</c>) so combat HUD updates allocate 0 B in steady state (ADR-0007).
    /// </summary>
    public static class UiThemeAndFontProvider
    {
        /// <summary>Deep tactical slate panel color (#0F172A, 88% opacity).</summary>
        public static readonly Color PanelDark = new Color32(15, 23, 42, 224);

        /// <summary>Elevated card surface color (#1E293B, 92% opacity).</summary>
        public static readonly Color CardSurface = new Color32(30, 41, 59, 235);

        /// <summary>Electric cyan accent (#00F5D4).</summary>
        public static readonly Color ElectricCyan = new Color32(0, 245, 212, 255);

        /// <summary>Solar amber highlight (#FFB703).</summary>
        public static readonly Color SolarAmber = new Color32(255, 183, 3, 255);

        /// <summary>Crimson danger/lethal accent (#E63946).</summary>
        public static readonly Color CrimsonDanger = new Color32(230, 57, 70, 255);

        /// <summary>Emerald healthy HP accent (#22C55E).</summary>
        public static readonly Color HealthyGreen = new Color32(34, 197, 94, 255);

        /// <summary>Whisper violet chat accent (#C77DFF).</summary>
        public static readonly Color WhisperViolet = new Color32(199, 125, 255, 255);

        /// <summary>Public chat crisp off-white (#F8F9FA).</summary>
        public static readonly Color PublicOffWhite = new Color32(248, 249, 250, 255);

        /// <summary>Deep slate modal backdrop (#0F172A, 96% opacity).</summary>
        public static readonly Color DeepSlate = new Color32(15, 23, 42, 245);

        /// <summary>Pure frost foreground text (#F8FAFC).</summary>
        public static readonly Color PureFrost = new Color32(248, 250, 252, 255);

        /// <summary>Muted steel secondary text (#94A3B8).</summary>
        public static readonly Color MutedSteel = new Color32(148, 163, 184, 255);

        /// <summary>Duplicate-key conflict swap row highlight (#FFB703).</summary>
        public static readonly Color SwapHighlightAmber = new Color32(255, 183, 3, 255);

        private const int MaxCachedDeciseconds = 100;
        private static readonly string[] CachedTenthSecondStrings = BuildTenthSecondLookup();
        private static readonly string[] CachedMatchClockStrings = BuildClockLookup();

        private static TMP_FontAsset _cachedFallbackFont;
        private static Sprite _cachedWhiteSprite;
        private static Sprite _cachedCircleSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachesOnDomainReload()
        {
            _cachedFallbackFont = null;
            _cachedWhiteSprite = null;
            _cachedCircleSprite = null;
        }

        /// <summary>
        /// Returns a valid <see cref="TMP_FontAsset"/> even in headless/test environments where TMP Essential Resources
        /// has not been manually imported via modal dialog.
        /// </summary>
        public static TMP_FontAsset GetSharedFont()
        {
            if (_cachedFallbackFont != null)
            {
                return _cachedFallbackFont;
            }

            if (Resources.Load("TMP Settings") != null && TMP_Settings.defaultFontAsset != null)
            {
                _cachedFallbackFont = TMP_Settings.defaultFontAsset;
                return _cachedFallbackFont;
            }

            TMP_FontAsset bundledFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (bundledFont != null)
            {
                _cachedFallbackFont = bundledFont;
                return _cachedFallbackFont;
            }

            Font osFont = Font.CreateDynamicFontFromOSFont("Arial", 24);
            if (osFont != null)
            {
                _cachedFallbackFont = TMP_FontAsset.CreateFontAsset(osFont);
                if (_cachedFallbackFont != null)
                {
                    _cachedFallbackFont.name = "RuntimeFallbackTMPFont";
                }
            }

            return _cachedFallbackFont;
        }

        /// <summary>Assigns <see cref="GetSharedFont"/> onto <paramref name="label"/> if its font is currently null.</summary>
        public static void EnsureFontAssigned(TMP_Text label)
        {
            if (label != null && label.font == null)
            {
                label.font = GetSharedFont();
            }
        }

        /// <summary>Returns a shared 8x8 solid white <see cref="Sprite"/> required for <c>Image.Type.Filled</c> bars.</summary>
        public static Sprite GetWhiteSprite()
        {
            if (_cachedWhiteSprite != null)
            {
                return _cachedWhiteSprite;
            }

            Texture2D tex = new Texture2D(8, 8, TextureFormat.RGBA32, mipChain: false)
            {
                name = "SharedWhiteSpriteTex",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color32[] pixels = new Color32[64];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            _cachedWhiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 100f);
            _cachedWhiteSprite.name = "SharedWhiteSprite";
            return _cachedWhiteSprite;
        }

        /// <summary>Returns a procedural anti-aliased circular disc <see cref="Sprite"/> for mobile buttons &amp; joysticks.</summary>
        public static Sprite GetCircleSprite()
        {
            if (_cachedCircleSprite != null)
            {
                return _cachedCircleSprite;
            }

            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "SharedCircleSpriteTex",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float radius = size * 0.47f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01((radius - dist) * 1.25f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            _cachedCircleSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            _cachedCircleSprite.name = "SharedCircleSprite";
            return _cachedCircleSprite;
        }

        /// <summary>
        /// Returns a pre-cached <c>"0.1"</c>..<c>"9.9"</c> string for cooldown overlays with 0 B heap allocation.
        /// </summary>
        public static string FormatCooldownTenthsZeroAlloc(float remainingSeconds)
        {
            if (remainingSeconds <= 0.04f)
            {
                return string.Empty;
            }

            int deciseconds = Mathf.Clamp(Mathf.CeilToInt(remainingSeconds * 10f), 1, MaxCachedDeciseconds - 1);
            return CachedTenthSecondStrings[deciseconds];
        }

        /// <summary>
        /// Returns a pre-cached <c>"MM:SS"</c> match clock string for <c>[0..300]</c> seconds with 0 B heap allocation.
        /// </summary>
        public static string FormatClockZeroAlloc(int wholeSeconds)
        {
            int clamped = Mathf.Clamp(wholeSeconds, 0, CachedMatchClockStrings.Length - 1);
            return CachedMatchClockStrings[clamped];
        }

        private static string[] BuildTenthSecondLookup()
        {
            string[] table = new string[MaxCachedDeciseconds];
            table[0] = string.Empty;
            for (int i = 1; i < MaxCachedDeciseconds; i++)
            {
                table[i] = $"{i / 10}.{i % 10}";
            }

            return table;
        }

        private static string[] BuildClockLookup()
        {
            string[] table = new string[301];
            for (int sec = 0; sec < table.Length; sec++)
            {
                int minutes = sec / 60;
                int seconds = sec % 60;
                table[sec] = $"{minutes:00}:{seconds:00}";
            }

            return table;
        }
    }
}
