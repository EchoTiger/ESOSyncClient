using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RedfurSync
{
    /// <summary>
    /// Shared theme constants, DPI helpers, palette repository, and font factories for all Fissal windows.
    /// Synchronized with web/v2/src/styles/tokens.css & themeSwitcher.helper.js.
    /// </summary>
    internal static class FissalTheme
    {
        public sealed class ThemePalette
        {
            public string Id { get; init; } = "fissal";
            public string DisplayName { get; init; } = "Fissal";
            public string Description { get; init; } = "Alfiq automaton emerald";
            public string Mark { get; init; } = "◈";
            public Color Bg { get; init; }
            public Color PanelBg { get; init; }
            public Color PanelBgAlt { get; init; }
            public Color Border { get; init; }
            public Color BorderSub { get; init; }
            public Color GoldBrt { get; init; }
            public Color GoldMid { get; init; }
            public Color GoldDim { get; init; }
            public Color GoldDark { get; init; }
            public Color Green { get; init; }
            public Color GreenDim { get; init; }
            public Color Text { get; init; }
            public Color TextSub { get; init; }
            public Color BarBg { get; init; }
            public Color BarDone { get; init; }
            public Color BarFail { get; init; }
            public Color Warn { get; init; }
            public Color BarActive { get; init; }
            public Color BarCancel { get; init; }
            public Color BtnBg { get; init; }
            public Color BtnBorder { get; init; }
            public Color ErrBg { get; init; }
            public Color ErrBorder { get; init; }
            public Color Sep { get; init; }
            public Color Accent { get; init; }
            public Color FocusAccent { get; init; }
        }

        private static readonly Dictionary<string, ThemePalette> _palettes = new(StringComparer.OrdinalIgnoreCase)
        {
            // ── 1. Fissal (Default) ────────────────────────────────────────────────────────
            ["fissal"] = new ThemePalette
            {
                Id          = "fissal",
                DisplayName = "Fissal",
                Description = "Alfiq automaton emerald & Dwemer brass",
                Mark        = "◈",
                Bg          = Color.FromArgb(10, 10, 8),      // #0a0a08
                PanelBg     = Color.FromArgb(20, 24, 16),     // #141810
                PanelBgAlt  = Color.FromArgb(29, 22, 12),     // #1d160c
                Border      = Color.FromArgb(90, 72, 36),     // #5a4824
                BorderSub   = Color.FromArgb(64, 50, 20),
                GoldBrt     = Color.FromArgb(212, 162, 78),   // #d4a24e
                GoldMid     = Color.FromArgb(160, 128, 55),
                GoldDim     = Color.FromArgb(95, 74, 30),
                GoldDark    = Color.FromArgb(64, 50, 20),
                Green       = Color.FromArgb(74, 222, 128),   // #4ade80
                GreenDim    = Color.FromArgb(28, 100, 50),
                Text        = Color.FromArgb(232, 215, 185),  // #e8d7b9
                TextSub     = Color.FromArgb(145, 128, 98),
                BarBg       = Color.FromArgb(30, 24, 14),
                BarDone     = Color.FromArgb(55, 185, 82),
                BarFail     = Color.FromArgb(255, 107, 107),  // #ff6b6b
                Warn        = Color.FromArgb(255, 203, 58),   // #ffcb3a
                BarActive   = Color.FromArgb(212, 162, 78),
                BarCancel   = Color.FromArgb(72, 65, 52),
                BtnBg       = Color.FromArgb(32, 26, 14),
                BtnBorder   = Color.FromArgb(88, 70, 34),
                ErrBg       = Color.FromArgb(38, 16, 10),
                ErrBorder   = Color.FromArgb(110, 40, 28),
                Sep         = Color.FromArgb(55, 44, 22),
                Accent      = Color.FromArgb(0, 255, 136),    // #00ff88
                FocusAccent = Color.FromArgb(0, 255, 136),
            },

            // ── 2. Amber ──────────────────────────────────────────────────────────────────
            ["amber"] = new ThemePalette
            {
                Id          = "amber",
                DisplayName = "Amber",
                Description = "Warm CRT phosphor earth & vintage almanac",
                Mark        = "✦",
                Bg          = Color.FromArgb(13, 9, 5),       // #0d0905
                PanelBg     = Color.FromArgb(22, 16, 10),     // #16100a
                PanelBgAlt  = Color.FromArgb(32, 22, 12),
                Border      = Color.FromArgb(95, 68, 28),     // #5f441c
                BorderSub   = Color.FromArgb(59, 41, 18),
                GoldBrt     = Color.FromArgb(255, 185, 56),   // #ffb938
                GoldMid     = Color.FromArgb(210, 145, 35),
                GoldDim     = Color.FromArgb(120, 80, 20),
                GoldDark    = Color.FromArgb(70, 45, 12),
                Green       = Color.FromArgb(255, 203, 58),   // #ffcb3a
                GreenDim    = Color.FromArgb(160, 112, 18),
                Text        = Color.FromArgb(255, 225, 160),
                TextSub     = Color.FromArgb(165, 130, 80),
                BarBg       = Color.FromArgb(28, 18, 10),
                BarDone     = Color.FromArgb(255, 185, 56),
                BarFail     = Color.FromArgb(255, 95, 75),
                Warn        = Color.FromArgb(255, 215, 80),
                BarActive   = Color.FromArgb(255, 154, 42),
                BarCancel   = Color.FromArgb(75, 55, 35),
                BtnBg       = Color.FromArgb(35, 22, 12),
                BtnBorder   = Color.FromArgb(115, 78, 30),
                ErrBg       = Color.FromArgb(42, 16, 10),
                ErrBorder   = Color.FromArgb(125, 45, 25),
                Sep         = Color.FromArgb(65, 45, 20),
                Accent      = Color.FromArgb(255, 185, 56),
                FocusAccent = Color.FromArgb(255, 234, 121),  // #ffea79
            },

            // ── 3. Ice / Cyan ─────────────────────────────────────────────────────────────
            ["cyan"] = new ThemePalette
            {
                Id          = "cyan",
                DisplayName = "Ice Cyan",
                Description = "Clinical lab cool phosphor terminal",
                Mark        = "✧",
                Bg          = Color.FromArgb(5, 10, 13),      // #050a0d
                PanelBg     = Color.FromArgb(10, 18, 24),     // #0a1218
                PanelBgAlt  = Color.FromArgb(14, 26, 36),
                Border      = Color.FromArgb(26, 44, 54),     // #1a2c36
                BorderSub   = Color.FromArgb(18, 32, 40),
                GoldBrt     = Color.FromArgb(34, 211, 238),   // #22d3ee
                GoldMid     = Color.FromArgb(24, 160, 185),
                GoldDim     = Color.FromArgb(16, 95, 115),
                GoldDark    = Color.FromArgb(10, 55, 70),
                Green       = Color.FromArgb(56, 189, 248),   // #38bdf8
                GreenDim    = Color.FromArgb(20, 95, 130),
                Text        = Color.FromArgb(197, 243, 251),  // #c5f3fb
                TextSub     = Color.FromArgb(91, 143, 163),   // #5b8fa3
                BarBg       = Color.FromArgb(10, 20, 28),
                BarDone     = Color.FromArgb(34, 211, 238),
                BarFail     = Color.FromArgb(255, 105, 130),
                Warn        = Color.FromArgb(255, 215, 107),  // #ffd76b
                BarActive   = Color.FromArgb(103, 232, 249),
                BarCancel   = Color.FromArgb(40, 60, 70),
                BtnBg       = Color.FromArgb(12, 24, 34),
                BtnBorder   = Color.FromArgb(34, 75, 95),
                ErrBg       = Color.FromArgb(35, 15, 20),
                ErrBorder   = Color.FromArgb(110, 35, 45),
                Sep         = Color.FromArgb(24, 48, 60),
                Accent      = Color.FromArgb(34, 211, 238),
                FocusAccent = Color.FromArgb(56, 189, 248),
            },

            // ── 4. Plasma ─────────────────────────────────────────────────────────────────
            ["plasma"] = new ThemePalette
            {
                Id          = "plasma",
                DisplayName = "Plasma",
                Description = "Arcade synthwave neon & magenta pulse",
                Mark        = "✣",
                Bg          = Color.FromArgb(10, 5, 9),       // #0a0509
                PanelBg     = Color.FromArgb(20, 10, 18),     // #140a12
                PanelBgAlt  = Color.FromArgb(30, 14, 26),
                Border      = Color.FromArgb(56, 24, 51),     // #381833
                BorderSub   = Color.FromArgb(40, 16, 36),
                GoldBrt     = Color.FromArgb(255, 77, 138),   // #ff4d8a
                GoldMid     = Color.FromArgb(200, 55, 110),
                GoldDim     = Color.FromArgb(120, 30, 65),
                GoldDark    = Color.FromArgb(70, 15, 40),
                Green       = Color.FromArgb(0, 240, 255),    // #00f0ff
                GreenDim    = Color.FromArgb(0, 120, 140),
                Text        = Color.FromArgb(255, 182, 212),  // #ffb6d4
                TextSub     = Color.FromArgb(166, 74, 120),   // #a64a78
                BarBg       = Color.FromArgb(24, 10, 22),
                BarDone     = Color.FromArgb(255, 77, 138),
                BarFail     = Color.FromArgb(255, 60, 80),
                Warn        = Color.FromArgb(255, 174, 107),
                BarActive   = Color.FromArgb(196, 137, 255),
                BarCancel   = Color.FromArgb(65, 40, 60),
                BtnBg       = Color.FromArgb(28, 12, 24),
                BtnBorder   = Color.FromArgb(85, 30, 75),
                ErrBg       = Color.FromArgb(40, 12, 20),
                ErrBorder   = Color.FromArgb(120, 30, 50),
                Sep         = Color.FromArgb(60, 24, 52),
                Accent      = Color.FromArgb(255, 77, 138),
                FocusAccent = Color.FromArgb(0, 240, 255),
            },

            // ── 5. Void ───────────────────────────────────────────────────────────────────
            ["void"] = new ThemePalette
            {
                Id          = "void",
                DisplayName = "Void",
                Description = "Midnight Senche deep violet & solar fire",
                Mark        = "☽",
                Bg          = Color.FromArgb(6, 5, 26),       // #06051a
                PanelBg     = Color.FromArgb(12, 10, 38),     // #0c0a26
                PanelBgAlt  = Color.FromArgb(20, 16, 54),
                Border      = Color.FromArgb(38, 29, 82),     // #261d52
                BorderSub   = Color.FromArgb(28, 20, 60),
                GoldBrt     = Color.FromArgb(168, 85, 247),   // #a855f7
                GoldMid     = Color.FromArgb(130, 60, 200),
                GoldDim     = Color.FromArgb(80, 35, 130),
                GoldDark    = Color.FromArgb(45, 18, 80),
                Green       = Color.FromArgb(210, 73, 31),    // #d2491f
                GreenDim    = Color.FromArgb(120, 40, 18),
                Text        = Color.FromArgb(216, 204, 255),  // #d8ccff
                TextSub     = Color.FromArgb(109, 87, 196),   // #6d57c4
                BarBg       = Color.FromArgb(16, 12, 45),
                BarDone     = Color.FromArgb(168, 85, 247),
                BarFail     = Color.FromArgb(255, 70, 70),
                Warn        = Color.FromArgb(255, 201, 122),
                BarActive   = Color.FromArgb(255, 133, 51),   // #ff8533
                BarCancel   = Color.FromArgb(50, 40, 80),
                BtnBg       = Color.FromArgb(22, 16, 52),
                BtnBorder   = Color.FromArgb(65, 45, 120),
                ErrBg       = Color.FromArgb(38, 12, 25),
                ErrBorder   = Color.FromArgb(115, 30, 60),
                Sep         = Color.FromArgb(45, 32, 90),
                Accent      = Color.FromArgb(210, 73, 31),
                FocusAccent = Color.FromArgb(255, 133, 51),
            },

            // ── 6. Phosphor ───────────────────────────────────────────────────────────────
            ["phosphor"] = new ThemePalette
            {
                Id          = "phosphor",
                DisplayName = "Phosphor",
                Description = "Brutalist P1 monochromatic CRT green",
                Mark        = "▣",
                Bg          = Color.FromArgb(0, 0, 0),        // #000000
                PanelBg     = Color.FromArgb(5, 9, 8),        // #050908
                PanelBgAlt  = Color.FromArgb(10, 18, 14),
                Border      = Color.FromArgb(26, 42, 24),     // #1a2a18
                BorderSub   = Color.FromArgb(18, 30, 16),
                GoldBrt     = Color.FromArgb(214, 255, 58),   // #d6ff3a
                GoldMid     = Color.FromArgb(160, 200, 35),
                GoldDim     = Color.FromArgb(94, 155, 0),
                GoldDark    = Color.FromArgb(50, 90, 0),
                Green       = Color.FromArgb(163, 255, 0),    // #a3ff00
                GreenDim    = Color.FromArgb(94, 155, 0),
                Text        = Color.FromArgb(163, 255, 0),    // #a3ff00
                TextSub     = Color.FromArgb(94, 155, 0),     // #5e9b00
                BarBg       = Color.FromArgb(8, 14, 8),
                BarDone     = Color.FromArgb(163, 255, 0),
                BarFail     = Color.FromArgb(255, 60, 60),
                Warn        = Color.FromArgb(240, 255, 0),
                BarActive   = Color.FromArgb(57, 255, 20),    // #39ff14
                BarCancel   = Color.FromArgb(30, 50, 30),
                BtnBg       = Color.FromArgb(10, 18, 10),
                BtnBorder   = Color.FromArgb(40, 80, 35),
                ErrBg       = Color.FromArgb(25, 10, 10),
                ErrBorder   = Color.FromArgb(80, 25, 25),
                Sep         = Color.FromArgb(24, 46, 22),
                Accent      = Color.FromArgb(214, 255, 58),
                FocusAccent = Color.FromArgb(57, 255, 20),
            },

            // ── 7. Khajiit ────────────────────────────────────────────────────────────────
            ["khajiit"] = new ThemePalette
            {
                Id          = "khajiit",
                DisplayName = "Khajiit",
                Description = "Desert dusk, moon-sugar, & starlight gold",
                Mark        = "☾",
                Bg          = Color.FromArgb(20, 8, 40),      // #140828
                PanelBg     = Color.FromArgb(29, 16, 24),     // #1d1018
                PanelBgAlt  = Color.FromArgb(38, 20, 36),
                Border      = Color.FromArgb(200, 122, 58),   // #c87a3a
                BorderSub   = Color.FromArgb(74, 40, 24),
                GoldBrt     = Color.FromArgb(240, 201, 138),  // #f0c98a
                GoldMid     = Color.FromArgb(184, 138, 82),
                GoldDim     = Color.FromArgb(120, 85, 45),
                GoldDark    = Color.FromArgb(65, 40, 20),
                Green       = Color.FromArgb(93, 255, 155),   // #5dff9b
                GreenDim    = Color.FromArgb(40, 120, 70),
                Text        = Color.FromArgb(240, 201, 138),  // #f0c98a
                TextSub     = Color.FromArgb(184, 138, 82),   // #b88a52
                BarBg       = Color.FromArgb(28, 14, 30),
                BarDone     = Color.FromArgb(93, 255, 155),
                BarFail     = Color.FromArgb(255, 100, 100),
                Warn        = Color.FromArgb(255, 154, 60),   // #ff9a3c
                BarActive   = Color.FromArgb(242, 156, 92),
                BarCancel   = Color.FromArgb(60, 40, 55),
                BtnBg       = Color.FromArgb(38, 20, 32),
                BtnBorder   = Color.FromArgb(140, 75, 40),
                ErrBg       = Color.FromArgb(40, 14, 25),
                ErrBorder   = Color.FromArgb(120, 35, 50),
                Sep         = Color.FromArgb(70, 38, 50),
                Accent      = Color.FromArgb(255, 154, 60),
                FocusAccent = Color.FromArgb(93, 255, 155),
            },

            // ── 8. Dunmer ─────────────────────────────────────────────────────────────────
            ["dunmer"] = new ThemePalette
            {
                Id          = "dunmer",
                DisplayName = "Dunmer",
                Description = "Vvardenfell ash, bonemold & blood-red banners",
                Mark        = "◆",
                Bg          = Color.FromArgb(10, 6, 6),       // #0a0606
                PanelBg     = Color.FromArgb(18, 10, 8),      // #120a08
                PanelBgAlt  = Color.FromArgb(28, 14, 12),
                Border      = Color.FromArgb(107, 30, 30),    // #6b1e1e
                BorderSub   = Color.FromArgb(60, 18, 18),
                GoldBrt     = Color.FromArgb(212, 168, 94),   // #d4a85e
                GoldMid     = Color.FromArgb(160, 120, 65),
                GoldDim     = Color.FromArgb(100, 70, 35),
                GoldDark    = Color.FromArgb(55, 35, 15),
                Green       = Color.FromArgb(47, 224, 200),   // #2fe0c8
                GreenDim    = Color.FromArgb(25, 110, 100),
                Text        = Color.FromArgb(212, 184, 142),  // #d4b88e
                TextSub     = Color.FromArgb(138, 112, 80),   // #8a7050
                BarBg       = Color.FromArgb(25, 12, 12),
                BarDone     = Color.FromArgb(47, 224, 200),
                BarFail     = Color.FromArgb(178, 29, 29),    // #b21d1d
                Warn        = Color.FromArgb(212, 168, 94),
                BarActive   = Color.FromArgb(178, 29, 29),
                BarCancel   = Color.FromArgb(60, 40, 40),
                BtnBg       = Color.FromArgb(28, 14, 12),
                BtnBorder   = Color.FromArgb(115, 38, 38),
                ErrBg       = Color.FromArgb(42, 10, 10),
                ErrBorder   = Color.FromArgb(130, 25, 25),
                Sep         = Color.FromArgb(60, 22, 22),
                Accent      = Color.FromArgb(178, 29, 29),
                FocusAccent = Color.FromArgb(47, 224, 200),
            },

            // ── 9. Orsimer ────────────────────────────────────────────────────────────────
            ["orsimer"] = new ThemePalette
            {
                Id          = "orsimer",
                DisplayName = "Orsimer",
                Description = "Forge-iron, steel-blue & tribal green",
                Mark        = "⚒",
                Bg          = Color.FromArgb(12, 15, 18),     // #0c0f12
                PanelBg     = Color.FromArgb(19, 23, 32),     // #131720
                PanelBgAlt  = Color.FromArgb(26, 32, 44),
                Border      = Color.FromArgb(106, 138, 154),  // #6a8a9a
                BorderSub   = Color.FromArgb(42, 46, 54),
                GoldBrt     = Color.FromArgb(184, 212, 220),  // #b8d4dc
                GoldMid     = Color.FromArgb(130, 160, 175),
                GoldDim     = Color.FromArgb(80, 105, 120),
                GoldDark    = Color.FromArgb(40, 60, 75),
                Green       = Color.FromArgb(93, 201, 122),   // #5dc97a
                GreenDim    = Color.FromArgb(45, 110, 65),
                Text        = Color.FromArgb(216, 228, 236),  // #d8e4ec
                TextSub     = Color.FromArgb(136, 160, 176),  // #88a0b0
                BarBg       = Color.FromArgb(18, 24, 32),
                BarDone     = Color.FromArgb(93, 201, 122),
                BarFail     = Color.FromArgb(235, 90, 90),
                Warn        = Color.FromArgb(216, 228, 236),
                BarActive   = Color.FromArgb(125, 255, 160),  // #7dffa0
                BarCancel   = Color.FromArgb(50, 60, 70),
                BtnBg       = Color.FromArgb(24, 30, 42),
                BtnBorder   = Color.FromArgb(75, 105, 120),
                ErrBg       = Color.FromArgb(35, 16, 20),
                ErrBorder   = Color.FromArgb(110, 35, 45),
                Sep         = Color.FromArgb(40, 55, 68),
                Accent      = Color.FromArgb(93, 201, 122),
                FocusAccent = Color.FromArgb(125, 255, 160),
            },

            // ── 10. Necrom ────────────────────────────────────────────────────────────────
            ["necrom"] = new ThemePalette
            {
                Id          = "necrom",
                DisplayName = "Necrom",
                Description = "Apocrypha violet, sickly bone & green ink",
                Mark        = "◌",
                Bg          = Color.FromArgb(5, 12, 8),       // #050c08
                PanelBg     = Color.FromArgb(8, 20, 16),      // #081410
                PanelBgAlt  = Color.FromArgb(14, 32, 24),
                Border      = Color.FromArgb(42, 106, 78),    // #2a6a4e
                BorderSub   = Color.FromArgb(20, 50, 36),
                GoldBrt     = Color.FromArgb(196, 216, 168),  // #c4d8a8
                GoldMid     = Color.FromArgb(140, 165, 115),
                GoldDim     = Color.FromArgb(80, 105, 65),
                GoldDark    = Color.FromArgb(40, 60, 35),
                Green       = Color.FromArgb(58, 255, 154),   // #3aff9a
                GreenDim    = Color.FromArgb(28, 125, 75),
                Text        = Color.FromArgb(168, 224, 184),  // #a8e0b8
                TextSub     = Color.FromArgb(94, 154, 114),   // #5e9a72
                BarBg       = Color.FromArgb(10, 24, 18),
                BarDone     = Color.FromArgb(58, 255, 154),
                BarFail     = Color.FromArgb(255, 80, 110),
                Warn        = Color.FromArgb(196, 216, 168),
                BarActive   = Color.FromArgb(207, 107, 255),  // #cf6bff
                BarCancel   = Color.FromArgb(35, 60, 45),
                BtnBg       = Color.FromArgb(12, 28, 22),
                BtnBorder   = Color.FromArgb(45, 115, 85),
                ErrBg       = Color.FromArgb(35, 12, 20),
                ErrBorder   = Color.FromArgb(110, 30, 55),
                Sep         = Color.FromArgb(30, 70, 50),
                Accent      = Color.FromArgb(58, 255, 154),
                FocusAccent = Color.FromArgb(207, 107, 255),
            },

            // ── 11. Blackwood ─────────────────────────────────────────────────────────────
            ["blackwood"] = new ThemePalette
            {
                Id          = "blackwood",
                DisplayName = "Blackwood",
                Description = "Marsh teal, fog blue & torch fire orange",
                Mark        = "♜",
                Bg          = Color.FromArgb(10, 16, 24),     // #0a1018
                PanelBg     = Color.FromArgb(15, 24, 34),     // #0f1822
                PanelBgAlt  = Color.FromArgb(22, 34, 48),
                Border      = Color.FromArgb(74, 98, 128),    // #4a6280
                BorderSub   = Color.FromArgb(40, 56, 74),
                GoldBrt     = Color.FromArgb(240, 160, 80),   // #f0a050
                GoldMid     = Color.FromArgb(180, 115, 55),
                GoldDim     = Color.FromArgb(110, 70, 30),
                GoldDark    = Color.FromArgb(60, 38, 15),
                Green       = Color.FromArgb(255, 140, 58),   // #ff8c3a
                GreenDim    = Color.FromArgb(140, 65, 20),
                Text        = Color.FromArgb(184, 200, 216),  // #b8c8d8
                TextSub     = Color.FromArgb(106, 128, 152),  // #6a8098
                BarBg       = Color.FromArgb(16, 26, 38),
                BarDone     = Color.FromArgb(255, 140, 58),
                BarFail     = Color.FromArgb(255, 85, 85),
                Warn        = Color.FromArgb(255, 179, 71),   // #ffb347
                BarActive   = Color.FromArgb(255, 179, 71),
                BarCancel   = Color.FromArgb(45, 60, 75),
                BtnBg       = Color.FromArgb(18, 30, 44),
                BtnBorder   = Color.FromArgb(65, 95, 125),
                ErrBg       = Color.FromArgb(36, 15, 20),
                ErrBorder   = Color.FromArgb(115, 35, 45),
                Sep         = Color.FromArgb(38, 55, 75),
                Accent      = Color.FromArgb(255, 140, 58),
                FocusAccent = Color.FromArgb(255, 179, 71),
            },

            // ── 12. Stained Glass (Guard-House Cathedral) ──────────────────────────────────
            ["stainedglass"] = new ThemePalette
            {
                Id          = "stainedglass",
                DisplayName = "Stained Glass",
                Description = "Obsidian glass, soul gem azure & clockwork amber",
                Mark        = "◈",
                Bg          = Color.FromArgb(7, 10, 15),       // #070a0f
                PanelBg     = Color.FromArgb(13, 19, 29),      // #0d131d
                PanelBgAlt  = Color.FromArgb(18, 25, 38),      // #121926
                Border      = Color.FromArgb(56, 189, 248),    // #38bdf8
                BorderSub   = Color.FromArgb(28, 95, 124),
                GoldBrt     = Color.FromArgb(245, 158, 11),    // #f59e0b
                GoldMid     = Color.FromArgb(217, 119, 6),
                GoldDim     = Color.FromArgb(180, 83, 9),
                GoldDark    = Color.FromArgb(120, 53, 15),
                Green       = Color.FromArgb(74, 222, 128),    // #4ade80
                GreenDim    = Color.FromArgb(34, 197, 94),
                Text        = Color.FromArgb(226, 232, 240),   // #e2e8f0
                TextSub     = Color.FromArgb(142, 159, 173),   // #8e9fad
                BarBg       = Color.FromArgb(15, 23, 42),
                BarDone     = Color.FromArgb(74, 222, 128),
                BarFail     = Color.FromArgb(248, 113, 113),   // #f87171
                Warn        = Color.FromArgb(245, 158, 11),
                BarActive   = Color.FromArgb(56, 189, 248),
                BarCancel   = Color.FromArgb(51, 65, 85),
                BtnBg       = Color.FromArgb(19, 28, 44),
                BtnBorder   = Color.FromArgb(56, 189, 248),
                ErrBg       = Color.FromArgb(45, 15, 20),
                ErrBorder   = Color.FromArgb(150, 40, 50),
                Sep         = Color.FromArgb(36, 50, 66),
                Accent      = Color.FromArgb(192, 132, 252),   // #c084fc
                FocusAccent = Color.FromArgb(56, 189, 248),
            }
        };

        public static IReadOnlyDictionary<string, ThemePalette> AllPalettes => _palettes;
        public static ThemePalette Current { get; private set; } = _palettes["fissal"];

        public static event Action? ThemeChanged;

        public static void SetTheme(string themeId)
        {
            if (string.IsNullOrWhiteSpace(themeId)) themeId = "fissal";
            if (_palettes.TryGetValue(themeId.Trim(), out var palette))
            {
                Current = palette;
            }
            else
            {
                Current = _palettes["fissal"];
            }

            try
            {
                var cfg = AppConfig.Instance;
                if (!string.Equals(cfg.Theme, Current.Id, StringComparison.OrdinalIgnoreCase))
                {
                    cfg.Theme = Current.Id;
                    cfg.Save();
                }
            }
            catch { }

            ThemeChanged?.Invoke();
        }

        // ── Dynamic Dynamic Palette Accessors (Backwards-Compatible) ────────
        public static Color CBg        => Current.Bg;
        public static Color CPanelBg   => Current.PanelBg;
        public static Color CPanelBgAlt=> Current.PanelBgAlt;
        public static Color CBorder    => Current.Border;
        public static Color CBorderSub => Current.BorderSub;
        public static Color CGoldBrt   => Current.GoldBrt;
        public static Color CGoldMid   => Current.GoldMid;
        public static Color CGoldDim   => Current.GoldDim;
        public static Color CGoldDark  => Current.GoldDark;
        public static Color CGreen     => Current.Green;
        public static Color CGreenDim  => Current.GreenDim;
        public static Color CText      => Current.Text;
        public static Color CTextSub   => Current.TextSub;
        public static Color CBarBg     => Current.BarBg;
        public static Color CBarDone   => Current.BarDone;
        public static Color CBarFail   => Current.BarFail;
        public static Color CWarn      => Current.Warn;
        public static Color CBarActive => Current.BarActive;
        public static Color CBarCancel => Current.BarCancel;
        public static Color CBtnBg     => Current.BtnBg;
        public static Color CBtnBorder => Current.BtnBorder;
        public static Color CErrBg     => Current.ErrBg;
        public static Color CErrBorder => Current.ErrBorder;
        public static Color CSep       => Current.Sep;
        public static Color CAccent    => Current.Accent;
        public static Color CFocusAccent=>Current.FocusAccent;
        public static string ThemeMark => Current.Mark;

        public static readonly Color CBtnDark  = Color.FromArgb(85, 85, 85); 
        public static readonly Color CBtnLight = Color.FromArgb(150, 150, 150);

        // ── P/Invoke — the only reliable way to get per-monitor DPI ──────────
        [DllImport("user32.dll")] private static extern int GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsProcessDPIAware();

        /// <summary>
        /// Returns the true DPI scale factor for the monitor a window is on.
        /// Call AFTER the form's Handle has been created.
        /// Falls back gracefully on older Windows versions.
        /// </summary>
        public static float GetScale(IntPtr hwnd)
        {
            try
            {
                int dpi = GetDpiForWindow(hwnd);
                if (dpi > 0) return dpi / 96f;
            }
            catch { /* GetDpiForWindow requires Win10 1607+ */ }

            // Fallback: read from a Graphics context on the window itself
            try
            {
                using var g = Graphics.FromHwnd(hwnd);
                return g.DpiX / 96f;
            }
            catch { }

            return 1f;
        }

        // ── Font stack ────────────────────────────────────────────────────────
                
        // P/Invoke to register the font globally for native controls (TextBox, Button)
        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

        private static readonly PrivateFontCollection _pfc = new PrivateFontCollection();
        private static readonly FontFamily? _customRetroFont;

        static FissalTheme()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string? actualResourceName = null;

                // The bulletproof hunt: dynamically locate embedded font
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith("RetroFontMain.ttf", StringComparison.OrdinalIgnoreCase))
                    {
                        actualResourceName = name;
                        break;
                    }
                }

                if (actualResourceName != null)
                {
                    using Stream? stream = assembly.GetManifestResourceStream(actualResourceName);
                    if (stream != null)
                    {
                        byte[] fontData = new byte[stream.Length];
                        stream.ReadExactly(fontData, 0, fontData.Length);

                        IntPtr dataPtr = Marshal.AllocCoTaskMem(fontData.Length);
                        try
                        {
                            Marshal.Copy(fontData, 0, dataPtr, fontData.Length);
                            _pfc.AddMemoryFont(dataPtr, fontData.Length);
                            if (_pfc.Families.Length > 0)
                            {
                                _customRetroFont = _pfc.Families[0];
                            }
                            uint cFonts = 0;
                            AddFontMemResourceEx(dataPtr, (uint)fontData.Length, IntPtr.Zero, ref cFonts);
                        }
                        finally
                        {
                            Marshal.FreeCoTaskMem(dataPtr);
                        }
                    }
                }
            }
            catch { }

            try
            {
                string savedTheme = AppConfig.Instance.Theme;
                if (!string.IsNullOrWhiteSpace(savedTheme) && _palettes.TryGetValue(savedTheme, out var p))
                {
                    Current = p;
                }
            }
            catch { }
        }

        public static string TitleFontName => _customRetroFont?.Name ?? "Segoe UI";
        public static string BodyFontName  => "Segoe UI";
        public static string MonoFontName  => "Consolas";

        // ── A quick sniff to find the main monitor's scale for the Tray Menu
        public static float GetSystemScale()
        {
            try
            {
                using var g = Graphics.FromHwnd(IntPtr.Zero);
                return g.DpiX / 96f;
            }
            catch { return 1f; }
        }

        // ── The Translation Magic
        public static float GlobalTextScale => Math.Max(0.75f, Math.Min(1.75f, AppConfig.Instance.AppScale));

        private static float PtToPx(float pt, float scale)
        {
            float px = pt * (96f / 72f) * scale * GlobalTextScale;
            return (float)Math.Round(px); 
        }

        /// <summary>
        /// Display / Heading font with vintage Dwemer / CRT feel.
        /// </summary>
        public static Font Title(float pt, float scale, FontStyle style = FontStyle.Regular)
        {
            float pxSize = Math.Max(8f, PtToPx(pt, scale));
            if (_customRetroFont != null)
                return new Font(_customRetroFont, pxSize, style, GraphicsUnit.Pixel);

            return new Font("Segoe UI", pxSize, style | FontStyle.Bold, GraphicsUnit.Pixel);
        }

        /// <summary>
        /// Highly legible standard UI body font (Segoe UI / Arial) for multi-line chat transcripts,
        /// settings fields, and form controls.
        /// </summary>
        public static Font Body(float pt, float scale, FontStyle style = FontStyle.Regular)
        {
            float pxSize = Math.Max(8f, PtToPx(pt, scale));
            try
            {
                return new Font("Segoe UI", pxSize, style, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font(FontFamily.GenericSansSerif, pxSize, style, GraphicsUnit.Pixel);
            }
        }

        /// <summary>
        /// Monospace font (Consolas / Courier New) for diagnostics, logs, and code blocks.
        /// </summary>
        public static Font Mono(float pt, float scale, FontStyle style = FontStyle.Regular)
        {
            float pxSize = Math.Max(8f, PtToPx(pt, scale));
            try
            {
                return new Font("Consolas", pxSize, style, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font(FontFamily.GenericMonospace, pxSize, style, GraphicsUnit.Pixel);
            }
        }

        // ── Decorative helpers ────────────────────────────────────────────────

        public static void DrawDivider(Graphics g, int x1, int x2, int y, Color lineColor, Color diamondColor)
        {
            using var linePen = new Pen(lineColor, 1f);
            g.DrawLine(linePen, x1, y, x2, y);
            
            int cx = (x1 + x2) / 2;
            using var diamondBrush = new SolidBrush(diamondColor);
            g.FillPolygon(diamondBrush, new[]
            {
                new Point(cx,     y - 3),
                new Point(cx + 4, y),
                new Point(cx,     y + 3),
                new Point(cx - 4, y),
            });
        }

        public static void DrawCornerRivets(Graphics g, int w, int h, int margin, Color rivetColor)
        {
            int r = 8;
            
            using var rivetBrush = new SolidBrush(rivetColor);
            using var highlightPen = new Pen(Color.FromArgb(80, Color.White), 0.5f);
            
            foreach (var (rx, ry) in new[] {
                (margin, margin), (w - margin - r, margin),
                (margin, h - margin - r), (w - margin - r, h - margin - r) })
            {
                g.FillEllipse(rivetBrush, rx, ry, r, r);
                g.DrawEllipse(highlightPen, rx, ry, r, r);
            }
        }

        /// <summary>
        /// Renders an authentic encased Dwemer terminal chassis with multi-layer bevels,
        /// corner mounting brackets, 3D rivets, and CRT recessed screen shadows.
        /// </summary>
        public static void DrawTerminalChassis(Graphics g, int w, int h, float scale)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 1. Outer Chassis Metal Border (Multi-tier industrial frame)
            using (var outerPen = new Pen(CGoldDark, 2f * scale))
            {
                g.DrawRectangle(outerPen, 1, 1, w - 2, h - 2);
            }
            using (var midPen = new Pen(CBorder, 1f * scale))
            {
                g.DrawRectangle(midPen, 3, 3, w - 6, h - 6);
            }
            using (var innerPen = new Pen(CBorderSub, 1f * scale))
            {
                g.DrawRectangle(innerPen, 5, 5, w - 10, h - 10);
            }

            // 2. Corner Reinforcement L-Plates (Dwemer brass corner braces)
            int braceSize = (int)(28 * scale);
            using var braceBrush = new SolidBrush(Color.FromArgb(50, CGoldDim));
            using var braceBorderPen = new Pen(CGoldMid, 1f);

            // Top-Left
            g.FillPolygon(braceBrush, new PointF[] {
                new PointF(2, 2), new PointF(braceSize, 2), new PointF(braceSize, 6),
                new PointF(6, 6), new PointF(6, braceSize), new PointF(2, braceSize) });
            g.DrawPolygon(braceBorderPen, new PointF[] {
                new PointF(2, 2), new PointF(braceSize, 2), new PointF(braceSize, 6),
                new PointF(6, 6), new PointF(6, braceSize), new PointF(2, braceSize) });

            // Top-Right
            g.FillPolygon(braceBrush, new PointF[] {
                new PointF(w - braceSize, 2), new PointF(w - 2, 2), new PointF(w - 2, braceSize),
                new PointF(w - 6, braceSize), new PointF(w - 6, 6), new PointF(w - braceSize, 6) });
            g.DrawPolygon(braceBorderPen, new PointF[] {
                new PointF(w - braceSize, 2), new PointF(w - 2, 2), new PointF(w - 2, braceSize),
                new PointF(w - 6, braceSize), new PointF(w - 6, 6), new PointF(w - braceSize, 6) });

            // Bottom-Left
            g.FillPolygon(braceBrush, new PointF[] {
                new PointF(2, h - braceSize), new PointF(6, h - braceSize), new PointF(6, h - 6),
                new PointF(braceSize, h - 6), new PointF(braceSize, h - 2), new PointF(2, h - 2) });
            g.DrawPolygon(braceBorderPen, new PointF[] {
                new PointF(2, h - braceSize), new PointF(6, h - braceSize), new PointF(6, h - 6),
                new PointF(braceSize, h - 6), new PointF(braceSize, h - 2), new PointF(2, h - 2) });

            // Bottom-Right
            g.FillPolygon(braceBrush, new PointF[] {
                new PointF(w - 6, h - braceSize), new PointF(w - 2, h - braceSize), new PointF(w - 2, h - 2),
                new PointF(w - braceSize, h - 2), new PointF(w - braceSize, h - 6), new PointF(w - 6, h - 6) });
            g.DrawPolygon(braceBorderPen, new PointF[] {
                new PointF(w - 6, h - braceSize), new PointF(w - 2, h - braceSize), new PointF(w - 2, h - 2),
                new PointF(w - braceSize, h - 2), new PointF(w - braceSize, h - 6), new PointF(w - 6, h - 6) });

            // 3. 3D Corner Rivets with specular glints & shadow wells
            int rivetRadius = (int)(7 * scale);
            int m = (int)(9 * scale);
            var rivetCenters = new[]
            {
                new Point(m, m),
                new Point(w - m - rivetRadius, m),
                new Point(m, h - m - rivetRadius),
                new Point(w - m - rivetRadius, h - m - rivetRadius),
                // Mid-perimeter decorative bolt studs
                new Point(w / 2 - rivetRadius / 2, m),
                new Point(w / 2 - rivetRadius / 2, h - m - rivetRadius)
            };

            foreach (var pt in rivetCenters)
            {
                // Shadow well
                using var shadowBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
                g.FillEllipse(shadowBrush, pt.X + 1, pt.Y + 1, rivetRadius, rivetRadius);

                // Brass bolt body
                using var boltBrush = new LinearGradientBrush(
                    new Rectangle(pt.X, pt.Y, rivetRadius, rivetRadius),
                    CGoldMid, CGoldDark, LinearGradientMode.ForwardDiagonal);
                g.FillEllipse(boltBrush, pt.X, pt.Y, rivetRadius, rivetRadius);

                // Specular highlight glint
                using var glintBrush = new SolidBrush(Color.FromArgb(200, Color.White));
                g.FillEllipse(glintBrush, pt.X + 1, pt.Y + 1, Math.Max(2, rivetRadius / 3), Math.Max(2, rivetRadius / 3));

                // Rim ring
                using var rimPen = new Pen(Color.FromArgb(120, CGoldBrt), 0.75f);
                g.DrawEllipse(rimPen, pt.X, pt.Y, rivetRadius, rivetRadius);
            }

            // 4. CRT Bezel Inner Shadow (Deep recessed cathode monitor illusion)
            int shadowDepth = (int)(16 * scale);
            using var topShadow = new LinearGradientBrush(
                new Rectangle(6, 6, w - 12, shadowDepth),
                Color.FromArgb(110, 0, 0, 0), Color.Transparent, LinearGradientMode.Vertical);
            g.FillRectangle(topShadow, 6, 6, w - 12, shadowDepth);

            using var bottomShadow = new LinearGradientBrush(
                new Rectangle(6, h - 6 - shadowDepth, w - 12, shadowDepth),
                Color.Transparent, Color.FromArgb(100, 0, 0, 0), LinearGradientMode.Vertical);
            g.FillRectangle(bottomShadow, 6, h - 6 - shadowDepth, w - 12, shadowDepth);
        }

        /// <summary>
        /// Renders an authentic physical Vacuum Tube / Nixie status lamp with industrial socket,
        /// glowing filament, hot burning gas core, and glass dome reflection glints.
        /// </summary>
        public static void DrawNixieLamp(Graphics g, Rectangle bounds, string label, Color lampColor, bool isActive, float phase, float scale)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int tubeDiameter = Math.Min(bounds.Width - 4, (int)(22 * scale));
            int tubeX = bounds.X + (bounds.Width - tubeDiameter) / 2;
            int tubeY = bounds.Y + 2;

            // 1. Industrial Outer Socket Ring
            using var socketShadow = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
            g.FillEllipse(socketShadow, tubeX + 1, tubeY + 1, tubeDiameter, tubeDiameter);

            using var socketBrush = new LinearGradientBrush(
                new Rectangle(tubeX, tubeY, tubeDiameter, tubeDiameter),
                Color.FromArgb(60, 55, 45), Color.FromArgb(15, 14, 12), LinearGradientMode.ForwardDiagonal);
            g.FillEllipse(socketBrush, tubeX, tubeY, tubeDiameter, tubeDiameter);

            using var socketRingPen = new Pen(CGoldDark, 1f);
            g.DrawEllipse(socketRingPen, tubeX, tubeY, tubeDiameter, tubeDiameter);

            // 2. Glass Bulb Void Cavity (Inner recessed socket)
            int glassPad = Math.Max(2, (int)(2 * scale));
            int bulbSize = tubeDiameter - glassPad * 2;
            int bulbX = tubeX + glassPad;
            int bulbY = tubeY + glassPad;

            using var voidBrush = new SolidBrush(Color.FromArgb(255, 3, 3, 5));
            g.FillEllipse(voidBrush, bulbX, bulbY, bulbSize, bulbSize);

            // 3. Glowing Filament & Trapped Plasma Gas (when active)
            if (isActive)
            {
                int pulseAlpha = (int)(160 + 60 * Math.Sin(phase));
                pulseAlpha = Math.Clamp(pulseAlpha, 90, 255);

                // Trapped gas halo
                using var gasPath = new GraphicsPath();
                gasPath.AddEllipse(bulbX + 1, bulbY + 1, bulbSize - 2, bulbSize - 2);
                using var gasBrush = new PathGradientBrush(gasPath)
                {
                    CenterColor = Color.FromArgb(pulseAlpha, lampColor),
                    SurroundColors = new[] { Color.Transparent },
                    FocusScales = new PointF(0.2f, 0.2f)
                };
                g.FillPath(gasBrush, gasPath);

                // Wire Filament
                int cx = bulbX + bulbSize / 2;
                using var hotWirePen = new Pen(Color.FromArgb(220, Color.White), 1f);
                g.DrawLine(hotWirePen, cx, bulbY + 3, cx, bulbY + bulbSize - 3);

                // Burning core
                using var coreBrush = new SolidBrush(Color.FromArgb(255, Color.White));
                g.FillEllipse(coreBrush, cx - 1, bulbY + bulbSize / 2 - 2, 3, 4);

                // Outer ambient glow ring
                using var glowPen = new Pen(Color.FromArgb(70, lampColor), 2f);
                g.DrawEllipse(glowPen, tubeX - 1, tubeY - 1, tubeDiameter + 2, tubeDiameter + 2);
            }
            else
            {
                // Cold dark filament
                int cx = bulbX + bulbSize / 2;
                using var wirePen = new Pen(Color.FromArgb(60, 40, 30), 1f);
                g.DrawLine(wirePen, cx, bulbY + 3, cx, bulbY + bulbSize - 3);
            }

            // 4. Glass Dome Glint & Crescent Specular Reflection
            using var topCrescent = new GraphicsPath();
            topCrescent.AddArc(bulbX + 1, bulbY + 1, bulbSize - 2, bulbSize - 2, 190, 160);
            topCrescent.AddArc(bulbX + 1, bulbY + 3, bulbSize - 2, bulbSize - 6, 350, -160);
            using var crescentBrush = new SolidBrush(Color.FromArgb(isActive ? 140 : 70, 255, 255, 255));
            g.FillPath(crescentBrush, topCrescent);

            // 5. Label text beneath the lamp
            int labelY = tubeY + tubeDiameter + 3;
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
            using var font = Mono(7f, scale, FontStyle.Bold);
            Color textCol = isActive ? lampColor : CTextSub;

            if (isActive)
            {
                DrawGlowingText(g, label, font, textCol, bounds.X + bounds.Width / 2f, labelY, 60, centered: true);
            }
            else
            {
                using var textBrush = new SolidBrush(textCol);
                g.DrawString(label, font, textBrush, new RectangleF(bounds.X, labelY, bounds.Width, bounds.Height - labelY), sf);
            }
        }

        /// <summary>
        /// Renders an animated cathode-ray oscilloscope displaying the live Tonal Lattice harmonics.
        /// </summary>
        public static void DrawTonalWaveform(Graphics g, Rectangle bounds, float phase, Color waveColor, bool isTransmitting, float scale)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 1. Recessed cathode screen backdrop
            using var bgBrush = new SolidBrush(Color.FromArgb(4, 9, 6));
            g.FillRectangle(bgBrush, bounds);

            // 2. Oscilloscope Reticle / Grid
            using var gridPen = new Pen(Color.FromArgb(25, CGreen), 1f);
            int midY = bounds.Y + bounds.Height / 2;
            g.DrawLine(gridPen, bounds.X, midY, bounds.Right, midY);
            for (int x = bounds.X + 20; x < bounds.Right; x += 25)
            {
                g.DrawLine(gridPen, x, bounds.Y, x, bounds.Bottom);
            }

            // 3. Mathematical Tonal Waveform Path
            int steps = Math.Max(20, bounds.Width / 2);
            var points = new PointF[steps];
            float amplitude = isTransmitting ? (bounds.Height * 0.38f) : (bounds.Height * 0.20f);
            float freq1 = isTransmitting ? 0.08f : 0.045f;
            float freq2 = isTransmitting ? 0.16f : 0.09f;

            for (int i = 0; i < steps; i++)
            {
                float x = bounds.X + (float)i / (steps - 1) * bounds.Width;
                float t = i + phase;
                float yVal = (float)(Math.Sin(t * freq1) * 0.7 + Math.Sin(t * freq2) * 0.3);
                points[i] = new PointF(x, midY + yVal * amplitude);
            }

            if (points.Length > 1)
            {
                // Ambient glow pass
                using var glowPen = new Pen(Color.FromArgb(60, waveColor), 3f * scale);
                g.DrawCurve(glowPen, points, 0.5f);

                // Sharp core beam
                using var beamPen = new Pen(Color.FromArgb(230, waveColor), 1.25f * scale);
                g.DrawCurve(beamPen, points, 0.5f);
            }

            // 4. CRT Outer Border & Glint
            using var borderPen = new Pen(CBorderSub, 1f);
            g.DrawRectangle(borderPen, bounds);
        }

        public static void DrawGlowingText(Graphics g, string text, Font font, Color color, float x, float y, int glowAlpha = 40, bool centered = false)
        {
            var sf = centered ? new StringFormat { Alignment = StringAlignment.Center } : StringFormat.GenericDefault;

            if (glowAlpha > 0)
            {
                using var glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, color));
                g.DrawString(text, font, glowBrush, new PointF(x, y - 1), sf);
                g.DrawString(text, font, glowBrush, new PointF(x, y + 1), sf);
                g.DrawString(text, font, glowBrush, new PointF(x - 1, y), sf);
                g.DrawString(text, font, glowBrush, new PointF(x + 1, y), sf);
            }
            using var coreBrush = new SolidBrush(color);
            g.DrawString(text, font, coreBrush, new PointF(x, y), sf);
        }

        public static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            p.AddArc(x,       y,       r*2, r*2, 180, 90);
            p.AddArc(x+w-r*2, y,       r*2, r*2, 270, 90);
            p.AddArc(x+w-r*2, y+h-r*2, r*2, r*2,   0, 90);
            p.AddArc(x,       y+h-r*2, r*2, r*2,  90, 90);
            p.CloseFigure();
            return p;
        }
    }
}