--[[
    FissalRelay_Console.lua
    Master Administrative Command Console for Fissal Relay Prime
    Crafted by Echo & Fissal for Fissal Relay and the Redfur Guilds.

    Features:
      • Unified 720x560 administrative bridge housing all staff tools
      • Draggable obsidian/brass bezel frame with persistent screen coordinates
      • Multi-Guild switcher tabs (Post, Dealers, Caravan, etc.)
      • High-signal Navigation Tab Bar:
          1. Guild Overview & Telemetry
          2. MotD Broadcast Studio
          3. Inactivity & Dues Auditor
          4. Kiosk Recon & Bids Vault
          5. TTC Guild Bumper
      • Quick action footer with real-time LibHistoire link status & instant synchronization
      • Keyboard shortcuts (ESC to close) and slash command navigation (/fr, /fissal)
]]--

FissalRelay = FissalRelay or {}
local FR = FissalRelay

local CONSOLE_WIDTH = 720
local CONSOLE_HEIGHT = 580

FR.selectedGuildIndex = 1
FR.activeConsoleTab = 1
FR.consoleTabs = {}

--[[ =========================================================================
     UTILITY & COLOR HELPERS
========================================================================= ]]--

local function ColorText(text, hexColor)
    return string.format("|c%s%s|r", hexColor, tostring(text or ""))
end

local function FormatGold(amount)
    amount = tonumber(amount) or 0
    if ZO_LocalizeDecimalNumber then
        return ZO_LocalizeDecimalNumber(amount)
    end
    local formatted = tostring(amount)
    while true do
        local k
        formatted, k = string.gsub(formatted, "^(-?%d+)(%d%d%d)", '%1,%2')
        if k == 0 then break end
    end
    return formatted
end

--[[ =========================================================================
     TACTILE BUTTON & TOOLTIP FACTORY
========================================================================= ]]--

function FR:StyleTactileButton(btn, opts)
    if not btn then return end
    opts = opts or {}
    local wm = WINDOW_MANAGER
    local normalBg = opts.normalBg or { 0.08, 0.08, 0.12, 0.90 }
    local hoverBg = opts.hoverBg or { 0.14, 0.14, 0.20, 0.95 }
    local normalEdge = opts.normalEdge or { 0.50, 0.38, 0.15, 0.80 }
    local hoverEdge = opts.hoverEdge or { 0.90, 0.70, 0.20, 1.00 }
    local normalText = opts.normalTextColor or { 0.85, 0.85, 0.85, 1 }
    local hoverText = opts.hoverTextColor or { 1, 1, 1, 1 }

    local bg = btn:GetNamedChild("Bg")
    if not bg then
        bg = wm:CreateControl("$(parent)_Bg", btn, CT_BACKDROP)
        bg:SetAnchorFill()
        bg:SetEdgeTexture("", 1, 1, 0)
    end
    bg:SetCenterColor(unpack(normalBg))
    bg:SetEdgeColor(unpack(normalEdge))
    btn.bg = bg

    btn:SetNormalFontColor(unpack(normalText))
    btn:SetMouseOverFontColor(unpack(hoverText))

    btn:SetHandler("OnMouseEnter", function(control)
        if not btn.isCustomActive then
            bg:SetCenterColor(unpack(hoverBg))
            bg:SetEdgeColor(unpack(hoverEdge))
        end
        if opts.tooltipTitle or opts.tooltipText then
            InitializeTooltip(InformationTooltip, control, TOP, 0, -4)
            local tip = ""
            if opts.tooltipTitle then
                tip = string.format("|cFF9900%s|r\n", opts.tooltipTitle)
            end
            if opts.tooltipText then
                tip = tip .. string.format("|cCCCCCC%s|r", opts.tooltipText)
            end
            SetTooltipText(InformationTooltip, tip)
        end
        if opts.onMouseEnter then opts.onMouseEnter(control) end
    end)

    btn:SetHandler("OnMouseExit", function(control)
        if not btn.isCustomActive then
            bg:SetCenterColor(unpack(normalBg))
            bg:SetEdgeColor(unpack(normalEdge))
        end
        if opts.tooltipTitle or opts.tooltipText then
            ClearTooltip(InformationTooltip)
        end
        if opts.onMouseExit then opts.onMouseExit(control) end
    end)

    return bg
end

--[[ =========================================================================
     CONSOLE INITIALIZATION & FRAME
========================================================================= ]]--

function FR:EnsureConsoleState()
    if not self.savedVars then return end
    if not self.savedVars.settings then self.savedVars.settings = {} end
    if not self.savedVars.settings.consolePos then
        self.savedVars.settings.consolePos = { x = 0, y = 0 }
    end
end

function FR:CreateConsoleUI()
    if self.consoleWindow then return end
    self:EnsureConsoleState()

    local wm = WINDOW_MANAGER

    -- 1. Main TopLevelWindow (Draggable, movable, clamped to screen)
    local console = wm:CreateTopLevelWindow("FissalRelay_Console")
    console:SetDimensions(CONSOLE_WIDTH, CONSOLE_HEIGHT)
    console:SetClampedToScreen(true)
    console:SetMouseEnabled(true)
    console:SetMovable(true)
    console:SetHidden(true)

    -- Position restoration
    local pos = self.savedVars.settings.consolePos
    console:ClearAnchors()
    if pos and pos.x and pos.y and (pos.x ~= 0 or pos.y ~= 0) then
        console:SetAnchor(TOPLEFT, GuiRoot, TOPLEFT, pos.x, pos.y)
    else
        console:SetAnchor(CENTER, GuiRoot, CENTER, 0, -10)
    end

    console:SetHandler("OnMoveStop", function(control)
        self.savedVars.settings.consolePos = {
            x = control:GetLeft(),
            y = control:GetTop(),
        }
    end)

    if UISpecialWindows then
        table.insert(UISpecialWindows, "FissalRelay_Console")
    end

    -- 2. Midnight Obsidian Backdrop with Burnished Brass Edge
    local backdrop = wm:CreateControl("$(parent)_Backdrop", console, CT_BACKDROP)
    backdrop:SetAnchorFill()
    backdrop:SetCenterColor(0.04, 0.04, 0.06, 0.96)
    backdrop:SetEdgeColor(0.75, 0.50, 0.10, 0.95)
    backdrop:SetEdgeTexture("", 8, 1, 0)

    -- 3. StatusMeter Munge Texture
    local munge = wm:CreateControl("$(parent)_Munge", console, CT_TEXTURE)
    munge:SetAnchorFill()
    munge:SetTexture("EsoUI/Art/Performance/StatusMeterMunge.dds")
    munge:SetAlpha(0.65)

    -- 4. Header Bar: Clockwork Guild Emblem
    local icon = wm:CreateControl("$(parent)_Icon", console, CT_TEXTURE)
    icon:SetAnchor(TOPLEFT, console, TOPLEFT, 14, 12)
    icon:SetDimensions(22, 22)
    icon:SetTexture("EsoUI/Art/MainMenu/menuBar_guilds_up.dds")

    -- 5. Close Button [x]
    local closeBtn = wm:CreateControl("$(parent)_Close", console, CT_BUTTON)
    closeBtn:SetAnchor(TOPRIGHT, console, TOPRIGHT, -12, 12)
    closeBtn:SetDimensions(20, 20)
    closeBtn:SetFont("ZoFontGameBold")
    closeBtn:SetNormalFontColor(0.7, 0.7, 0.7, 1)
    closeBtn:SetMouseOverFontColor(1, 0.3, 0.3, 1)
    closeBtn:SetText("x")
    closeBtn:SetHandler("OnClicked", function()
        self:ToggleConsole(false)
    end)

    -- 6. Title & Version (Centered vertically between icon and close button)
    local title = wm:CreateControl("$(parent)_Title", console, CT_LABEL)
    title:SetAnchor(LEFT, icon, RIGHT, 8, 0)
    title:SetAnchor(RIGHT, closeBtn, LEFT, -8, 0)
    title:SetHeight(22)
    title:SetVerticalAlignment(TEXT_ALIGN_CENTER)
    title:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
    title:SetFont("ZoFontGameBold")
    title:SetText("|cFF9900FISSAL RELAY PRIME|r  |c00FFCCCOMMAND CONSOLE|r  |c888888v1.5.1|r")

    -- 7. Top Divider Line
    local topDiv = wm:CreateControl("$(parent)_DivTop", console, CT_TEXTURE)
    topDiv:SetAnchor(TOPLEFT, console, TOPLEFT, 10, 42)
    topDiv:SetAnchor(TOPRIGHT, console, TOPRIGHT, -10, 42)
    topDiv:SetHeight(1)
    topDiv:SetColor(0.8, 0.5, 0.1, 0.4)

    -- 8. Guild Switcher Tabs (Row 1 under title)
    self.guildButtons = {}
    local numGuilds = math.min(math.max(GetNumGuilds(), 1), 5)
    local tabWidth = math.floor((CONSOLE_WIDTH - 24) / numGuilds)

    for i = 1, 5 do
        local gBtn = wm:CreateControl("$(parent)_GuildTab_" .. i, console, CT_BUTTON)
        gBtn:SetAnchor(TOPLEFT, console, TOPLEFT, 12 + (i - 1) * tabWidth, 48)
        gBtn:SetDimensions(tabWidth - 4, 24)
        gBtn:SetFont("ZoFontGameSmall")
        self:StyleTactileButton(gBtn, {
            normalBg = { 0.05, 0.05, 0.07, 0.85 },
            hoverBg = { 0.14, 0.12, 0.08, 0.95 },
            normalEdge = { 0.35, 0.28, 0.18, 0.65 },
            hoverEdge = { 0.90, 0.70, 0.20, 1.0 },
            normalTextColor = { 0.7, 0.7, 0.7, 1 },
            hoverTextColor = { 1, 0.9, 0.4, 1 },
            tooltipTitle = "Guild Switcher",
            tooltipText = "Switch active administrative context to this guild.",
        })
        gBtn:SetHandler("OnClicked", function()
            self:SelectConsoleGuild(i)
        end)
        self.guildButtons[i] = gBtn
    end

    -- 9. Navigation Tab Bar (Row 2: Overview, MotD Studio, Auditor, Kiosk Recon, TTC, Auto-Ranks)
    local navNames = {
        { id = 1, label = "Overview", desc = "Live guild telemetry, roster count, trader kiosk status, and LibHistoire pipeline health." },
        { id = 2, label = "MotD Studio", desc = "Compose, preview, and broadcast Guild Message of the Day with dynamic live tokens." },
        { id = 3, label = "Auditor", desc = "Inactivity and dues auditor to flag inactive members, check bank history, and send check-in mail." },
        { id = 4, label = "Kiosk Recon", desc = "Kiosk bids ledger and ground reconnaissance vault tracking trader hires and field observations." },
        { id = 5, label = "TTC Bumper", desc = "Scan and refresh your guild store listings on TamrielTradeCentre.com." },
        { id = 6, label = "Auto-Ranks", desc = "Automate member promotions and demotions based on sales or donations with full preview and safety guards." },
    }

    self.navButtons = {}
    local navWidth = math.floor((CONSOLE_WIDTH - 24) / #navNames)

    for idx, nav in ipairs(navNames) do
        local nBtn = wm:CreateControl("$(parent)_NavTab_" .. nav.id, console, CT_BUTTON)
        nBtn:SetAnchor(TOPLEFT, console, TOPLEFT, 12 + (idx - 1) * navWidth, 78)
        nBtn:SetDimensions(navWidth - 4, 26)
        nBtn:SetFont("ZoFontGameBold")
        nBtn:SetText(nav.label)
        self:StyleTactileButton(nBtn, {
            normalBg = { 0.06, 0.06, 0.09, 0.85 },
            hoverBg = { 0.08, 0.16, 0.18, 0.95 },
            normalEdge = { 0.25, 0.25, 0.30, 0.60 },
            hoverEdge = { 0, 0.90, 0.80, 1.0 },
            normalTextColor = { 0.7, 0.7, 0.7, 1 },
            hoverTextColor = { 0, 1, 0.9, 1 },
            tooltipTitle = nav.label,
            tooltipText = nav.desc,
        })
        nBtn:SetHandler("OnClicked", function()
            self:SelectConsoleTab(nav.id)
        end)
        self.navButtons[nav.id] = nBtn
    end

    -- 10. Nav Divider
    local navDiv = wm:CreateControl("$(parent)_DivNav", console, CT_TEXTURE)
    navDiv:SetAnchor(TOPLEFT, console, TOPLEFT, 10, 110)
    navDiv:SetAnchor(TOPRIGHT, console, TOPRIGHT, -10, 110)
    navDiv:SetHeight(1)
    navDiv:SetColor(0.8, 0.5, 0.1, 0.4)

    -- 11. Central Tab Content Container
    local content = wm:CreateControl("$(parent)_Content", console, CT_CONTROL)
    content:SetAnchor(TOPLEFT, console, TOPLEFT, 12, 116)
    content:SetAnchor(BOTTOMRIGHT, console, BOTTOMRIGHT, -12, -36)
    self.consoleContent = content

    -- 12. Bottom Divider Line
    local botDiv = wm:CreateControl("$(parent)_DivBot", console, CT_TEXTURE)
    botDiv:SetAnchor(BOTTOMLEFT, console, BOTTOMLEFT, 10, -34)
    botDiv:SetAnchor(BOTTOMRIGHT, console, BOTTOMRIGHT, -10, -34)
    botDiv:SetHeight(1)
    botDiv:SetColor(0.8, 0.5, 0.1, 0.4)

    -- 13. Footer Status Bar
    local statusLbl = wm:CreateControl("$(parent)_StatusLbl", console, CT_LABEL)
    statusLbl:SetAnchor(BOTTOMLEFT, console, BOTTOMLEFT, 14, -10)
    statusLbl:SetFont("ZoFontGameSmall")
    statusLbl:SetText("|c59E08A[ON] Connected|r | LibHistoire Aligned | Sales: 0")
    self.consoleStatusLbl = statusLbl

    -- 14. Footer Quick Action Buttons
    local syncBtn = wm:CreateControl("$(parent)_SyncBtn", console, CT_BUTTON)
    syncBtn:SetAnchor(BOTTOMRIGHT, console, BOTTOMRIGHT, -190, -6)
    syncBtn:SetDimensions(85, 24)
    syncBtn:SetFont("ZoFontGameSmall")
    syncBtn:SetText("Sync Now")
    self:StyleTactileButton(syncBtn, {
        normalBg = { 0.04, 0.12, 0.10, 0.90 },
        hoverBg = { 0.06, 0.18, 0.14, 0.98 },
        normalEdge = { 0, 0.70, 0.55, 0.80 },
        hoverEdge = { 0, 1.00, 0.80, 1.00 },
        normalTextColor = { 0, 1, 0.8, 1 },
        hoverTextColor = { 0.4, 1, 0.9, 1 },
        tooltipTitle = "Full Guild Synchronization",
        tooltipText = "Run comprehensive sync: LibHistoire event pump, kiosk registry scan, and member roster snapshot in one click.",
    })
    syncBtn:SetHandler("OnClicked", function()
        self:SetupProcessors()
        self:PumpLibHistoire(true)
        self:ScanOwnedKiosks()
        self:TakeRosterSnapshot()
        self:UpdateConsoleStatus()
        self.PrintChat("Relay synchronization completed across all guild channels.")
    end)

    local mailBtn = wm:CreateControl("$(parent)_MailBtn", console, CT_BUTTON)
    mailBtn:SetAnchor(BOTTOMRIGHT, console, BOTTOMRIGHT, -98, -6)
    mailBtn:SetDimensions(85, 24)
    mailBtn:SetFont("ZoFontGameSmall")
    mailBtn:SetText("Mail Assist")
    self:StyleTactileButton(mailBtn, {
        normalBg = { 0.12, 0.10, 0.04, 0.90 },
        hoverBg = { 0.18, 0.14, 0.06, 0.98 },
        normalEdge = { 0.75, 0.55, 0.10, 0.80 },
        hoverEdge = { 1.00, 0.85, 0.20, 1.00 },
        normalTextColor = { 1, 0.85, 0.2, 1 },
        hoverTextColor = { 1, 0.95, 0.5, 1 },
        tooltipTitle = "Open Mail Assistant",
        tooltipText = "Dock Fissal's Mail Assistant window to automate raffle payouts, inactivity warnings, and welcome letters.",
    })
    mailBtn:SetHandler("OnClicked", function()
        if self.ToggleRaffleMailUI then
            self:ToggleRaffleMailUI(true)
        end
    end)

    local settingsBtn = wm:CreateControl("$(parent)_SettingsBtn", console, CT_BUTTON)
    settingsBtn:SetAnchor(BOTTOMRIGHT, console, BOTTOMRIGHT, -12, -6)
    settingsBtn:SetDimensions(80, 24)
    settingsBtn:SetFont("ZoFontGameSmall")
    settingsBtn:SetText("Settings")
    self:StyleTactileButton(settingsBtn, {
        normalBg = { 0.08, 0.08, 0.10, 0.90 },
        hoverBg = { 0.14, 0.14, 0.16, 0.98 },
        normalEdge = { 0.40, 0.40, 0.45, 0.70 },
        hoverEdge = { 0.75, 0.75, 0.85, 1.00 },
        normalTextColor = { 0.8, 0.8, 0.8, 1 },
        hoverTextColor = { 1, 1, 1, 1 },
        tooltipTitle = "AddOn Settings",
        tooltipText = "Open the LibAddonMenu settings panel to configure automation thresholds, HUD positioning, and features.",
    })
    settingsBtn:SetHandler("OnClicked", function()
        if LibAddonMenu2 then
            LibAddonMenu2:OpenToPanel(FissalRelay_Options)
        end
    end)

    self.consoleWindow = console

    -- Initialize Overview Tab (Tab 1)
    self:BuildOverviewTab(content)

    -- Defer other tabs build if modules are present
    if self.BuildMotDStudio then self:BuildMotDStudio(content) end
    if self.BuildAuditorUI then self:BuildAuditorUI(content) end
    if self.BuildBidsReconUI then self:BuildBidsReconUI(content) end
    if self.BuildTTCBumperTab then self:BuildTTCBumperTab(content) end
    if self.BuildAutoRanksUI then self:BuildAutoRanksUI(content) end

    self:SelectConsoleGuild(1)
    self:SelectConsoleTab(1)
    self:UpdateConsoleStatus()
end

--[[ =========================================================================
     TAB 1: GUILD OVERVIEW & TELEMETRY
========================================================================= ]]--

function FR:BuildOverviewTab(parent)
    local wm = WINDOW_MANAGER
    local panel = wm:CreateControl("FissalRelay_Console_Tab1", parent, CT_CONTROL)
    panel:SetAnchorFill()
    panel:SetHidden(false)
    self.consoleTabs[1] = panel

    -- Card 1: Guild Identity & Trader Kiosk
    local card1 = wm:CreateControl("$(parent)_Card1", panel, CT_BACKDROP)
    card1:SetAnchor(TOPLEFT, panel, TOPLEFT, 0, 0)
    card1:SetDimensions(342, 185)
    card1:SetCenterColor(0.06, 0.06, 0.08, 0.85)
    card1:SetEdgeColor(0.30, 0.25, 0.18, 0.70)
    card1:SetEdgeTexture("", 8, 1, 0)

    local c1Title = wm:CreateControl("$(parent)_Title", card1, CT_LABEL)
    c1Title:SetAnchor(TOPLEFT, card1, TOPLEFT, 10, 8)
    c1Title:SetFont("ZoFontGameBold")
    c1Title:SetText(ColorText("GUILD & KIOSK TELEMETRY", "FF9900"))

    local gNameLbl = wm:CreateControl("$(parent)_GuildName", card1, CT_LABEL)
    gNameLbl:SetAnchor(TOPLEFT, card1, TOPLEFT, 10, 32)
    gNameLbl:SetAnchor(TOPRIGHT, card1, TOPRIGHT, -10, 32)
    gNameLbl:SetFont("ZoFontGameMedium")
    gNameLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
    gNameLbl:SetText("Guild: --")
    self.overviewGuildNameLbl = gNameLbl

    local gMembersLbl = wm:CreateControl("$(parent)_Members", card1, CT_LABEL)
    gMembersLbl:SetAnchor(TOPLEFT, card1, TOPLEFT, 10, 56)
    gMembersLbl:SetAnchor(TOPRIGHT, card1, TOPRIGHT, -10, 56)
    gMembersLbl:SetFont("ZoFontGame")
    gMembersLbl:SetText("Members: -- (Online: --)")
    self.overviewMembersLbl = gMembersLbl

    local gKioskLbl = wm:CreateControl("$(parent)_Kiosk", card1, CT_LABEL)
    gKioskLbl:SetAnchor(TOPLEFT, card1, TOPLEFT, 10, 80)
    gKioskLbl:SetAnchor(TOPRIGHT, card1, TOPRIGHT, -10, 80)
    gKioskLbl:SetFont("ZoFontGame")
    gKioskLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
    gKioskLbl:SetText("Kiosk: Scanning...")
    self.overviewKioskLbl = gKioskLbl

    local gPermsLbl = wm:CreateControl("$(parent)_Perms", card1, CT_LABEL)
    gPermsLbl:SetAnchor(TOPLEFT, card1, TOPLEFT, 10, 106)
    gPermsLbl:SetAnchor(TOPRIGHT, card1, TOPRIGHT, -10, 106)
    gPermsLbl:SetFont("ZoFontGameSmall")
    gPermsLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
    gPermsLbl:SetText("Staff Permissions: Checking...")
    self.overviewPermsLbl = gPermsLbl

    local snapBtn = wm:CreateControl("$(parent)_SnapBtn", card1, CT_BUTTON)
    snapBtn:SetAnchor(BOTTOMLEFT, card1, BOTTOMLEFT, 10, -10)
    snapBtn:SetDimensions(150, 24)
    snapBtn:SetFont("ZoFontGameSmall")
    snapBtn:SetText("Snapshot Roster")
    self:StyleTactileButton(snapBtn, {
        normalBg = { 0.04, 0.12, 0.12, 0.90 },
        hoverBg = { 0.06, 0.18, 0.18, 0.98 },
        normalEdge = { 0, 0.75, 0.65, 0.80 },
        hoverEdge = { 0, 1.0, 0.85, 1.0 },
        normalTextColor = { 0, 1, 0.8, 1 },
        hoverTextColor = { 0.4, 1, 0.9, 1 },
        tooltipTitle = "Snapshot Member Roster",
        tooltipText = "Capture an instantaneous record of all member handles, ranks, and notes across all your guilds for auditing.",
    })
    snapBtn:SetHandler("OnClicked", function()
        local count = self:TakeRosterSnapshot()
        self.PrintChat(string.format("Roster snapshot captured for %d guild(s)!", count))
        self:UpdateOverviewTab()
    end)

    local scanKioskBtn = wm:CreateControl("$(parent)_ScanKioskBtn", card1, CT_BUTTON)
    scanKioskBtn:SetAnchor(BOTTOMRIGHT, card1, BOTTOMRIGHT, -10, -10)
    scanKioskBtn:SetDimensions(150, 24)
    scanKioskBtn:SetFont("ZoFontGameSmall")
    scanKioskBtn:SetText("Scan Kiosks")
    self:StyleTactileButton(scanKioskBtn, {
        normalBg = { 0.12, 0.10, 0.04, 0.90 },
        hoverBg = { 0.18, 0.14, 0.06, 0.98 },
        normalEdge = { 0.75, 0.55, 0.10, 0.80 },
        hoverEdge = { 1.00, 0.85, 0.20, 1.00 },
        normalTextColor = { 1, 0.85, 0.2, 1 },
        hoverTextColor = { 1, 0.95, 0.5, 1 },
        tooltipTitle = "Scan Owned Kiosks",
        tooltipText = "Query Tamriel's trading kiosk registry to identify current trader locations and hired merchants.",
    })
    scanKioskBtn:SetHandler("OnClicked", function()
        local found = self:ScanOwnedKiosks()
        self.PrintChat(string.format("Scanned owned kiosks: %d verified.", found))
        self:UpdateOverviewTab()
    end)

    -- Card 2: LibHistoire & Relay Synchronization
    local card2 = wm:CreateControl("$(parent)_Card2", panel, CT_BACKDROP)
    card2:SetAnchor(TOPRIGHT, panel, TOPRIGHT, 0, 0)
    card2:SetDimensions(342, 185)
    card2:SetCenterColor(0.06, 0.06, 0.08, 0.85)
    card2:SetEdgeColor(0.30, 0.25, 0.18, 0.70)
    card2:SetEdgeTexture("", 8, 1, 0)

    local c2Title = wm:CreateControl("$(parent)_Title", card2, CT_LABEL)
    c2Title:SetAnchor(TOPLEFT, card2, TOPLEFT, 10, 8)
    c2Title:SetFont("ZoFontGameBold")
    c2Title:SetText(ColorText("HISTOIRE COURIER RELAY", "00FFCC"))

    local syncStateLbl = wm:CreateControl("$(parent)_SyncState", card2, CT_LABEL)
    syncStateLbl:SetAnchor(TOPLEFT, card2, TOPLEFT, 10, 32)
    syncStateLbl:SetAnchor(TOPRIGHT, card2, TOPRIGHT, -10, 32)
    syncStateLbl:SetFont("ZoFontGame")
    syncStateLbl:SetText("Histoire Status: Checking...")
    self.overviewSyncStateLbl = syncStateLbl

    local pendingLbl = wm:CreateControl("$(parent)_Pending", card2, CT_LABEL)
    pendingLbl:SetAnchor(TOPLEFT, card2, TOPLEFT, 10, 56)
    pendingLbl:SetAnchor(TOPRIGHT, card2, TOPRIGHT, -10, 56)
    pendingLbl:SetFont("ZoFontGame")
    pendingLbl:SetText("Pending Ingestion: 0 events")
    self.overviewPendingLbl = pendingLbl

    local speedLbl = wm:CreateControl("$(parent)_Speed", card2, CT_LABEL)
    speedLbl:SetAnchor(TOPLEFT, card2, TOPLEFT, 10, 80)
    speedLbl:SetAnchor(TOPRIGHT, card2, TOPRIGHT, -10, 80)
    speedLbl:SetFont("ZoFontGameSmall")
    speedLbl:SetText("Ingestion Speed: 0 events/sec")
    self.overviewSpeedLbl = speedLbl

    local recordsLbl = wm:CreateControl("$(parent)_Records", card2, CT_LABEL)
    recordsLbl:SetAnchor(TOPLEFT, card2, TOPLEFT, 10, 106)
    recordsLbl:SetAnchor(TOPRIGHT, card2, TOPRIGHT, -10, 106)
    recordsLbl:SetFont("ZoFontGameSmall")
    recordsLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
    recordsLbl:SetText("Stored Sales: 0 | Bank Deposits: 0")
    self.overviewRecordsLbl = recordsLbl

    local turboBtn = wm:CreateControl("$(parent)_TurboBtn", card2, CT_BUTTON)
    turboBtn:SetAnchor(BOTTOMLEFT, card2, BOTTOMLEFT, 10, -10)
    turboBtn:SetDimensions(150, 24)
    turboBtn:SetFont("ZoFontGameSmall")
    turboBtn:SetText("Turbo Pump")
    self:StyleTactileButton(turboBtn, {
        normalBg = { 0.04, 0.12, 0.06, 0.90 },
        hoverBg = { 0.06, 0.18, 0.10, 0.98 },
        normalEdge = { 0.20, 0.75, 0.35, 0.80 },
        hoverEdge = { 0.30, 1.00, 0.50, 1.00 },
        normalTextColor = { 0.3, 1, 0.5, 1 },
        hoverTextColor = { 0.6, 1, 0.7, 1 },
        tooltipTitle = "Turbo LibHistoire Ingestion",
        tooltipText = "Accelerate LibHistoire event ingestion across all guild lines at maximum speed to catch up on missed sales and bank logs.",
    })
    turboBtn:SetHandler("OnClicked", function()
        self:PumpLibHistoire(true)
        self.PrintChat("LibHistoire Turbo Pumper activated across all guild channels!")
        self:UpdateOverviewTab()
    end)

    local resetPosBtn = wm:CreateControl("$(parent)_ResetPosBtn", card2, CT_BUTTON)
    resetPosBtn:SetAnchor(BOTTOMRIGHT, card2, BOTTOMRIGHT, -10, -10)
    resetPosBtn:SetDimensions(150, 24)
    resetPosBtn:SetFont("ZoFontGameSmall")
    resetPosBtn:SetText("Align Windows")
    self:StyleTactileButton(resetPosBtn, {
        normalBg = { 0.08, 0.08, 0.10, 0.90 },
        hoverBg = { 0.14, 0.14, 0.16, 0.98 },
        normalEdge = { 0.40, 0.40, 0.45, 0.70 },
        hoverEdge = { 0.75, 0.75, 0.85, 1.00 },
        normalTextColor = { 0.75, 0.75, 0.75, 1 },
        hoverTextColor = { 1, 1, 1, 1 },
        tooltipTitle = "Realign Interface Windows",
        tooltipText = "Reset all Fissal Relay window positions (Console, HUD, Raffle Mail) back to their default screen anchors.",
    })
    resetPosBtn:SetHandler("OnClicked", function()
        if self.ResetHUDPosition then self:ResetHUDPosition() end
        self.savedVars.settings.consolePos = { x = 0, y = 0 }
        self.consoleWindow:ClearAnchors()
        self.consoleWindow:SetAnchor(CENTER, GuiRoot, CENTER, 0, -10)
        self.PrintChat("All Fissal interface positions realigned to default.")
    end)

    -- Card 3: Live Raffle Metrics (Bottom Span)
    local card3 = wm:CreateControl("$(parent)_Card3", panel, CT_BACKDROP)
    card3:SetAnchor(TOPLEFT, card1, BOTTOMLEFT, 0, 10)
    card3:SetAnchor(BOTTOMRIGHT, panel, BOTTOMRIGHT, 0, 0)
    card3:SetCenterColor(0.06, 0.06, 0.08, 0.85)
    card3:SetEdgeColor(0.30, 0.25, 0.18, 0.70)
    card3:SetEdgeTexture("", 8, 1, 0)

    local c3Title = wm:CreateControl("$(parent)_Title", card3, CT_LABEL)
    c3Title:SetAnchor(TOPLEFT, card3, TOPLEFT, 10, 8)
    c3Title:SetFont("ZoFontGameBold")
    c3Title:SetText(ColorText("WEEKLY RAFFLE & TREASURY DISPATCH", "FFD700"))

    local potLbl = wm:CreateControl("$(parent)_Pot", card3, CT_LABEL)
    potLbl:SetAnchor(TOPLEFT, card3, TOPLEFT, 10, 36)
    potLbl:SetFont("ZoFontGameBold")
    potLbl:SetText("Pot: |cFFD7000 gold|r")
    self.overviewPotLbl = potLbl

    local tixLbl = wm:CreateControl("$(parent)_Tickets", card3, CT_LABEL)
    tixLbl:SetAnchor(TOPLEFT, card3, TOPLEFT, 240, 36)
    tixLbl:SetFont("ZoFontGameBold")
    tixLbl:SetText("Tickets: |c00FFCC0|r")
    self.overviewTixLbl = tixLbl

    local entLbl = wm:CreateControl("$(parent)_Entrants", card3, CT_LABEL)
    entLbl:SetAnchor(TOPLEFT, card3, TOPLEFT, 450, 36)
    entLbl:SetFont("ZoFontGameBold")
    entLbl:SetText("Entrants: |cFFFFFF0 members|r")
    self.overviewEntLbl = entLbl

    local prizesLbl = wm:CreateControl("$(parent)_Prizes", card3, CT_LABEL)
    prizesLbl:SetAnchor(TOPLEFT, card3, TOPLEFT, 10, 66)
    prizesLbl:SetFont("ZoFontGame")
    prizesLbl:SetText("Prizes: 1st: -- | 2nd: -- | 3rd: -- | Guild: --")
    self.overviewPrizesLbl = prizesLbl

    local winnersLbl = wm:CreateControl("$(parent)_Winners", card3, CT_LABEL)
    winnersLbl:SetAnchor(TOPLEFT, card3, TOPLEFT, 10, 92)
    winnersLbl:SetAnchor(BOTTOMRIGHT, card3, BOTTOMRIGHT, -10, -40)
    winnersLbl:SetFont("ZoFontGameSmall")
    winnersLbl:SetText("Winners: Checking ledger...")
    self.overviewWinnersLbl = winnersLbl

    local openRaffleBtn = wm:CreateControl("$(parent)_OpenRaffleBtn", card3, CT_BUTTON)
    openRaffleBtn:SetAnchor(BOTTOMLEFT, card3, BOTTOMLEFT, 10, -8)
    openRaffleBtn:SetDimensions(200, 26)
    openRaffleBtn:SetFont("ZoFontGameBold")
    openRaffleBtn:SetText("Open Raffle Assistant")
    self:StyleTactileButton(openRaffleBtn, {
        normalBg = { 0.12, 0.10, 0.04, 0.90 },
        hoverBg = { 0.18, 0.14, 0.06, 0.98 },
        normalEdge = { 0.75, 0.55, 0.10, 0.80 },
        hoverEdge = { 1.00, 0.85, 0.20, 1.00 },
        normalTextColor = { 1, 0.85, 0.2, 1 },
        hoverTextColor = { 1, 0.95, 0.5, 1 },
        tooltipTitle = "Open Raffle Assistant",
        tooltipText = "Open the dedicated Raffle Mail window to distribute prizes with zero-blue-border automated gold attachment.",
    })
    openRaffleBtn:SetHandler("OnClicked", function()
        if self.ToggleRaffleMailUI then
            self:ToggleRaffleMailUI(true)
        end
    end)

    local refreshRaffleBtn = wm:CreateControl("$(parent)_RefreshRaffleBtn", card3, CT_BUTTON)
    refreshRaffleBtn:SetAnchor(BOTTOMRIGHT, card3, BOTTOMRIGHT, -10, -8)
    refreshRaffleBtn:SetDimensions(160, 26)
    refreshRaffleBtn:SetFont("ZoFontGameSmall")
    refreshRaffleBtn:SetText("Refresh Ledger")
    self:StyleTactileButton(refreshRaffleBtn, {
        normalBg = { 0.04, 0.12, 0.12, 0.90 },
        hoverBg = { 0.06, 0.18, 0.18, 0.98 },
        normalEdge = { 0, 0.75, 0.65, 0.80 },
        hoverEdge = { 0, 1.0, 0.85, 1.0 },
        normalTextColor = { 0, 1, 0.8, 1 },
        hoverTextColor = { 0.4, 1, 0.9, 1 },
        tooltipTitle = "Refresh Raffle Ledger",
        tooltipText = "Recalculate pot totals, ticket counts, and member rankings from the latest guild bank records.",
    })
    refreshRaffleBtn:SetHandler("OnClicked", function()
        self:UpdateOverviewTab()
        self.PrintChat("Raffle metrics and ledger status refreshed.")
    end)
end

--[[ =========================================================================
     TAB 5: TTC BUMPER STUDIO EMBED
========================================================================= ]]--

function FR:BuildTTCBumperTab(parent)
    local wm = WINDOW_MANAGER
    local panel = wm:CreateControl("FissalRelay_Console_Tab5", parent, CT_CONTROL)
    panel:SetAnchorFill()
    panel:SetHidden(true)
    self.consoleTabs[5] = panel

    local card = wm:CreateControl("$(parent)_Card", panel, CT_BACKDROP)
    card:SetAnchorFill()
    card:SetCenterColor(0.06, 0.06, 0.08, 0.85)
    card:SetEdgeColor(0.30, 0.25, 0.18, 0.70)
    card:SetEdgeTexture("", 8, 1, 0)

    local title = wm:CreateControl("$(parent)_Title", card, CT_LABEL)
    title:SetAnchor(TOPLEFT, card, TOPLEFT, 14, 12)
    title:SetFont("ZoFontGameBold")
    title:SetText(ColorText("TAMRIEL TRADE CENTRE GUILD STORE BUMPER", "00FFCC"))

    local desc = wm:CreateControl("$(parent)_Desc", card, CT_LABEL)
    desc:SetAnchor(TOPLEFT, card, TOPLEFT, 14, 40)
    desc:SetAnchor(TOPRIGHT, card, TOPRIGHT, -14, 40)
    desc:SetFont("ZoFontGame")
    desc:SetText("The TTC Bumper scans your guild stores and updates public listing prices on TamrielTradeCentre.com.\nYou can launch a standalone docked bumper session or run immediate store synchronization below.")

    local startBtn = wm:CreateControl("$(parent)_StartBtn", card, CT_BUTTON)
    startBtn:SetAnchor(TOPLEFT, card, TOPLEFT, 14, 90)
    startBtn:SetDimensions(240, 32)
    startBtn:SetFont("ZoFontGameBold")
    startBtn:SetText("Launch TTC Bumper Window")
    self:StyleTactileButton(startBtn, {
        normalBg = { 0.04, 0.12, 0.12, 0.90 },
        hoverBg = { 0.06, 0.18, 0.18, 0.98 },
        normalEdge = { 0, 0.75, 0.65, 0.80 },
        hoverEdge = { 0, 1.0, 0.85, 1.0 },
        normalTextColor = { 0, 1, 0.8, 1 },
        hoverTextColor = { 0.4, 1, 0.9, 1 },
        tooltipTitle = "Launch TTC Bumper Window",
        tooltipText = "Open the TTC Bumper window to scan and refresh your guild store listings on TamrielTradeCentre.com.",
    })
    startBtn:SetHandler("OnClicked", function()
        if self.ToggleBumperUI then
            self:ToggleBumperUI(true)
        elseif self.StartBump then
            self:StartBump()
        end
    end)

    -- Automation Options Section
    local autoBumpBtn = wm:CreateControl("$(parent)_AutoBumpBtn", card, CT_BUTTON)
    autoBumpBtn:SetAnchor(TOPLEFT, card, TOPLEFT, 14, 135)
    autoBumpBtn:SetDimensions(320, 26)
    autoBumpBtn:SetFont("ZoFontGame")

    local function UpdateAutoBumpBtn()
        local isAuto = self.savedVars and self.savedVars.settings and self.savedVars.settings.showBumper
        autoBumpBtn:SetText(string.format("Auto-Show at Guild Store: %s",
            isAuto and ColorText("Enabled", "59E08A") or ColorText("Disabled", "888888")))
    end
    UpdateAutoBumpBtn()
    self:StyleTactileButton(autoBumpBtn, {
        normalBg = { 0.05, 0.08, 0.08, 0.85 },
        hoverBg = { 0.08, 0.12, 0.12, 0.95 },
        normalEdge = { 0.35, 0.30, 0.20, 0.65 },
        hoverEdge = { 0, 0.85, 0.75, 0.90 },
        normalTextColor = { 0.9, 0.9, 0.9, 1 },
        hoverTextColor = { 1, 1, 1, 1 },
        tooltipTitle = "Auto-Show at Guild Store",
        tooltipText = "Automatically open the Bumper selection window whenever you access a Guild Store.",
    })
    autoBumpBtn:SetHandler("OnClicked", function()
        self.savedVars.settings.showBumper = not self.savedVars.settings.showBumper
        UpdateAutoBumpBtn()
        if self.UpdateBumperUI then self:UpdateBumperUI() end
    end)

    local autoReloadBtn = wm:CreateControl("$(parent)_AutoReloadBtn", card, CT_BUTTON)
    autoReloadBtn:SetAnchor(TOPLEFT, card, TOPLEFT, 14, 168)
    autoReloadBtn:SetDimensions(320, 26)
    autoReloadBtn:SetFont("ZoFontGame")

    local function UpdateAutoReloadBtn()
        local isAR = self.savedVars and self.savedVars.settings and self.savedVars.settings.bumperAutoReload
        autoReloadBtn:SetText(string.format("Auto-Reload UI After Bump: %s",
            isAR and ColorText("Enabled", "59E08A") or ColorText("Disabled", "888888")))
    end
    UpdateAutoReloadBtn()
    self:StyleTactileButton(autoReloadBtn, {
        normalBg = { 0.05, 0.08, 0.08, 0.85 },
        hoverBg = { 0.08, 0.12, 0.12, 0.95 },
        normalEdge = { 0.35, 0.30, 0.20, 0.65 },
        hoverEdge = { 0, 0.85, 0.75, 0.90 },
        normalTextColor = { 0.9, 0.9, 0.9, 1 },
        hoverTextColor = { 1, 1, 1, 1 },
        tooltipTitle = "Automatically Reload UI After Bump",
        tooltipText = "Automatically execute /reloadui once all selected guild stores are bumped to flush listings to disk for TTC.",
    })
    autoReloadBtn:SetHandler("OnClicked", function()
        self.savedVars.settings.bumperAutoReload = not (self.savedVars.settings.bumperAutoReload)
        UpdateAutoReloadBtn()
        if self.UpdateBumperUI then self:UpdateBumperUI() end
    end)

    local waitLHBtn = wm:CreateControl("$(parent)_WaitLHBtn", card, CT_BUTTON)
    waitLHBtn:SetAnchor(TOPLEFT, card, TOPLEFT, 14, 201)
    waitLHBtn:SetDimensions(320, 26)
    waitLHBtn:SetFont("ZoFontGame")

    local function UpdateWaitLHBtn()
        local isAR = self.savedVars and self.savedVars.settings and self.savedVars.settings.bumperAutoReload
        local isWait = self.savedVars and self.savedVars.settings and self.savedVars.settings.bumperWaitForLibHistoire
        if not isAR then
            waitLHBtn:SetText(string.format("Wait for LibHistoire: %s", ColorText("Off (Requires Auto-Reload)", "555555")))
        else
            waitLHBtn:SetText(string.format("Wait for LibHistoire: %s",
                isWait and ColorText("Enabled", "00FFCC") or ColorText("Disabled", "888888")))
        end
    end
    UpdateWaitLHBtn()
    self:StyleTactileButton(waitLHBtn, {
        normalBg = { 0.05, 0.08, 0.08, 0.85 },
        hoverBg = { 0.08, 0.12, 0.12, 0.95 },
        normalEdge = { 0.35, 0.30, 0.20, 0.65 },
        hoverEdge = { 0, 0.85, 0.75, 0.90 },
        normalTextColor = { 0.9, 0.9, 0.9, 1 },
        hoverTextColor = { 1, 1, 1, 1 },
        tooltipTitle = "Wait for LibHistoire Requests",
        tooltipText = "When auto-reloading UI after a bump, wait for any active LibHistoire guild history requests or event queues to settle before reloading.",
    })
    waitLHBtn:SetHandler("OnClicked", function()
        self.savedVars.settings.bumperWaitForLibHistoire = not (self.savedVars.settings.bumperWaitForLibHistoire)
        UpdateWaitLHBtn()
        if self.UpdateBumperUI then self:UpdateBumperUI() end
    end)

    local bumpStatus = wm:CreateControl("$(parent)_Status", card, CT_LABEL)
    bumpStatus:SetAnchor(TOPLEFT, card, TOPLEFT, 14, 238)
    bumpStatus:SetFont("ZoFontGameSmall")
    bumpStatus:SetText("TTC Addon Status: " .. (TamrielTradeCentre and ColorText("Detected & Active", "59E08A") or ColorText("Not Installed", "FF5555")))
end

--[[ =========================================================================
     TAB & GUILD SELECTION CONTROLLER
========================================================================= ]]--

function FR:SelectConsoleGuild(guildIndex)
    local rawGuilds = GetNumGuilds()
    local numGuilds = math.min(math.max(rawGuilds, 1), 5)
    if guildIndex < 1 or guildIndex > rawGuilds then guildIndex = 1 end
    self.selectedGuildIndex = guildIndex

    local tabWidth = math.floor((CONSOLE_WIDTH - 24) / numGuilds)

    -- Update guild buttons visual state and dynamically distribute horizontally
    for i = 1, 5 do
        local btn = self.guildButtons and self.guildButtons[i]
        if btn then
            if i <= rawGuilds and i <= 5 then
                local gId = GetGuildId(i)
                local name = GetGuildName(gId)
                btn:ClearAnchors()
                btn:SetAnchor(TOPLEFT, self.consoleWindow, TOPLEFT, 12 + (i - 1) * tabWidth, 48)
                btn:SetDimensions(tabWidth - 4, 24)
                btn:SetHidden(false)
                btn:SetText(name ~= "" and name or ("Guild " .. i))
                if btn.bg then
                    if i == guildIndex then
                        btn.isCustomActive = true
                        btn.bg:SetCenterColor(0.18, 0.12, 0.04, 0.95)
                        btn.bg:SetEdgeColor(0.95, 0.70, 0.15, 1.0)
                        btn:SetNormalFontColor(1, 0.85, 0.2, 1)
                    else
                        btn.isCustomActive = false
                        btn.bg:SetCenterColor(0.05, 0.05, 0.07, 0.85)
                        btn.bg:SetEdgeColor(0.35, 0.28, 0.18, 0.65)
                        btn:SetNormalFontColor(0.65, 0.65, 0.65, 1)
                    end
                end
            else
                btn:SetHidden(true)
            end
        end
    end

    -- Update the active tab with the new guild context
    if self.activeConsoleTab == 1 then
        self:UpdateOverviewTab()
    elseif self.activeConsoleTab == 2 and self.UpdateMotDUI then
        self:UpdateMotDUI(true)
    elseif self.activeConsoleTab == 3 and self.UpdateAuditorUI then
        self:UpdateAuditorUI()
    elseif self.activeConsoleTab == 4 and self.UpdateBidsUI then
        self:UpdateBidsUI()
    elseif self.activeConsoleTab == 6 and self.UpdateAutoRanksUI then
        self:UpdateAutoRanksUI()
    end
end

function FR:SelectConsoleTab(tabId)
    self.activeConsoleTab = tabId

    -- Highlight active nav button
    for id, btn in pairs(self.navButtons or {}) do
        if btn.bg then
            if id == tabId then
                btn.isCustomActive = true
                btn.bg:SetCenterColor(0.04, 0.15, 0.16, 0.95)
                btn.bg:SetEdgeColor(0, 0.90, 0.80, 1.0)
                btn:SetNormalFontColor(0, 1, 0.8, 1)
            else
                btn.isCustomActive = false
                btn.bg:SetCenterColor(0.06, 0.06, 0.09, 0.85)
                btn.bg:SetEdgeColor(0.25, 0.25, 0.30, 0.60)
                btn:SetNormalFontColor(0.6, 0.6, 0.6, 1)
            end
        end
    end

    -- Switch visible panel
    for id, panel in pairs(self.consoleTabs or {}) do
        if id == tabId then
            panel:SetHidden(false)
        else
            panel:SetHidden(true)
        end
    end

    -- Trigger refresh
    if tabId == 1 then
        self:UpdateOverviewTab()
    elseif tabId == 2 and self.UpdateMotDUI then
        self:UpdateMotDUI(false)
    elseif tabId == 3 and self.UpdateAuditorUI then
        self:UpdateAuditorUI()
    elseif tabId == 4 and self.UpdateBidsUI then
        self:UpdateBidsUI()
    elseif tabId == 6 and self.UpdateAutoRanksUI then
        self:UpdateAutoRanksUI()
    end
end

--[[ =========================================================================
     LIVE TELEMETRY UPDATERS
========================================================================= ]]--

function FR:UpdateOverviewTab()
    if not self.consoleWindow or self.consoleWindow:IsHidden() then return end
    local gIdx = self.selectedGuildIndex or 1
    local numGuilds = GetNumGuilds()
    if gIdx < 1 or gIdx > numGuilds then return end

    local guildId = GetGuildId(gIdx)
    local guildName = GetGuildName(guildId)
    local memberCount = GetNumGuildMembers(guildId)
    local onlineCount = 0
    for m = 1, memberCount do
        local _, _, _, playerStatus, secsSinceLogoff = GetGuildMemberInfo(guildId, m)
        if (playerStatus ~= PLAYER_STATUS_OFFLINE) and (secsSinceLogoff == 0) then
            onlineCount = onlineCount + 1
        end
    end

    -- Guild Card
    if self.overviewGuildNameLbl then
        self.overviewGuildNameLbl:SetText(string.format("Guild: %s (ID: %d)", ColorText(guildName, "00FFCC"), guildId))
    end
    if self.overviewMembersLbl then
        self.overviewMembersLbl:SetText(string.format("Members: %s total | %s online",
            ColorText(tostring(memberCount), "FFFFFF"), ColorText(tostring(onlineCount), "59E08A")))
    end

    -- Kiosk detection
    local kioskInfo = "None (No Trader Hired)"
    if self.savedVars and self.savedVars.kiosks then
        for trader, data in pairs(self.savedVars.kiosks) do
            if data.guildName == guildName then
                local loc = (data.city and data.city ~= "") and data.city or (data.zone or "Tamriel")
                kioskInfo = string.format("%s in %s", ColorText(trader, "59E08A"), loc)
                break
            end
        end
    end
    if self.overviewKioskLbl then
        self.overviewKioskLbl:SetText("Kiosk: " .. kioskInfo)
    end

    -- Staff Permissions
    local hasMotD = DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_SET_MOTD)
    local hasBank = DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_BANK_VIEW_DEPOSIT_HISTORY)
    local hasClaim = DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_CLAIM_KIOSK)
    if self.overviewPermsLbl then
        self.overviewPermsLbl:SetText(string.format("Staff: MotD %s | Bank %s | Kiosks %s",
            hasMotD and ColorText("[YES]", "59E08A") or ColorText("[NO]", "888888"),
            hasBank and ColorText("[YES]", "59E08A") or ColorText("[NO]", "888888"),
            hasClaim and ColorText("[YES]", "59E08A") or ColorText("[NO]", "888888")))
    end

    -- LibHistoire Status (O(1) lookup)
    local saleCount = self:GetCount("sales")
    local depositCount = self:GetCount("deposits")
    if self.overviewRecordsLbl then
        self.overviewRecordsLbl:SetText(string.format("Stored Sales: %s | Bank Deposits: %s",
            ColorText(FormatGold(saleCount), "00FFCC"), ColorText(FormatGold(depositCount), "FFAA00")))
    end

    -- Pending Events & Link State
    local totalCategories, linkedCategories, totalPendingEvents, totalSpeed = 0, 0, 0, 0
    if LibHistoire and LibHistoire.internal and LibHistoire.internal.historyCache then
        for _, cat in ipairs({ GUILD_HISTORY_EVENT_CATEGORY_TRADER, GUILD_HISTORY_EVENT_CATEGORY_BANKED_CURRENCY }) do
            totalCategories = totalCategories + 1
            local cache = LibHistoire.internal.historyCache:GetCategoryCache(guildId, cat)
            if cache and cache.HasLinked and cache:HasLinked() then
                linkedCategories = linkedCategories + 1
            end
        end
    end
    for _, proc in pairs(self.processors or {}) do
        if proc.GetPendingEventMetrics then
            local remaining, speed = proc:GetPendingEventMetrics()
            if remaining and remaining > 0 then totalPendingEvents = totalPendingEvents + remaining end
            if speed and speed > 0 then totalSpeed = totalSpeed + speed end
        end
    end

    if self.overviewSyncStateLbl then
        local isLinked = linkedCategories >= totalCategories and totalCategories > 0
        self.overviewSyncStateLbl:SetText(string.format("Histoire Status: %s (%d/%d Linked)",
            isLinked and ColorText("Synchronized", "59E08A") or ColorText("Connecting...", "FFCC00"),
            linkedCategories, totalCategories))
    end
    if self.overviewPendingLbl then
        self.overviewPendingLbl:SetText(string.format("Pending Ingestion: %s events",
            ColorText(FormatGold(totalPendingEvents), totalPendingEvents > 0 and "FFCC00" or "59E08A")))
    end
    if self.overviewSpeedLbl then
        self.overviewSpeedLbl:SetText(string.format("Ingestion Speed: %s events/sec",
            ColorText(tostring(totalSpeed), "00FFCC")))
    end

    -- Raffle Metrics Card (Prioritizes live bank ledger with sealed fallback)
    local isPost = string.find(guildName, "Post") ~= nil
    local isDealers = string.find(guildName, "Dealer") ~= nil
    local gKey = isPost and "post" or (isDealers and "dealers" or nil)
    local raffleData = gKey and self.GetRaffleData and self:GetRaffleData(gKey)
    local liveMetrics = self.CalculateRaffleMetrics and self:CalculateRaffleMetrics(guildId, 7, 1000)

    if liveMetrics and liveMetrics.totalGold > 0 then
        if self.overviewPotLbl then
            self.overviewPotLbl:SetText(string.format("Pot: %s gold |c59E08A(Live Bank Ledger)|r", ColorText(FormatGold(liveMetrics.totalGold), "FFD700")))
        end
        if self.overviewTixLbl then
            self.overviewTixLbl:SetText(string.format("Tickets: %s", ColorText(FormatGold(liveMetrics.totalTickets), "00FFCC")))
        end
        if self.overviewEntLbl then
            self.overviewEntLbl:SetText(string.format("Entrants: %s members (%d deposits)", ColorText(tostring(liveMetrics.entrants), "FFFFFF"), liveMetrics.entries))
        end
        if self.overviewPrizesLbl then
            local pFirst = math.floor(liveMetrics.totalGold * 0.30)
            local pSecond = math.floor(liveMetrics.totalGold * 0.20)
            local pThird = math.floor(liveMetrics.totalGold * 0.10)
            local pGuild = math.floor(liveMetrics.totalGold * 0.40)
            self.overviewPrizesLbl:SetText(string.format("Projected: 1st: %s | 2nd: %s | 3rd: %s | Guild: %s",
                ColorText(FormatGold(pFirst), "FFD700"),
                ColorText(FormatGold(pSecond), "FFAA00"),
                ColorText(FormatGold(pThird), "FF8800"),
                ColorText(FormatGold(pGuild), "00FFCC")))
        end
        if self.overviewWinnersLbl then
            if raffleData and raffleData.winners and #raffleData.winners > 0 then
                local winText = string.format("Prior Week Winners (%s):\n", raffleData.weekLabel or "Sealed")
                for _, w in ipairs(raffleData.winners) do
                    winText = winText .. string.format("  #%d %s - %s gold (Ticket #%d)\n",
                        w.place, ColorText(w.name, "00FFCC"), FormatGold(w.prize), w.ticket)
                end
                self.overviewWinnersLbl:SetText(winText)
            else
                self.overviewWinnersLbl:SetText("Drawing Sunday! Live entries actively recording from guild bank.")
            end
        end
    elseif raffleData then
        if self.overviewPotLbl then
            self.overviewPotLbl:SetText(string.format("Pot: %s gold |c888888(Sealed Prior Week)|r", ColorText(FormatGold(raffleData.pot or 0), "FFD700")))
        end
        if self.overviewTixLbl then
            self.overviewTixLbl:SetText(string.format("Tickets: %s", ColorText(FormatGold(raffleData.tickets or 0), "00FFCC")))
        end
        if self.overviewEntLbl then
            self.overviewEntLbl:SetText(string.format("Entrants: %s members", ColorText(tostring(raffleData.entrants or 0), "FFFFFF")))
        end
        if self.overviewPrizesLbl and raffleData.prizes then
            local p = raffleData.prizes
            self.overviewPrizesLbl:SetText(string.format("Prizes: 1st: %s | 2nd: %s | 3rd: %s | Guild: %s",
                ColorText(FormatGold(p.first or 0), "FFD700"),
                ColorText(FormatGold(p.second or 0), "FFAA00"),
                ColorText(FormatGold(p.third or 0), "FF8800"),
                ColorText(FormatGold(p.guild or 0), "00FFCC")))
        end
        if self.overviewWinnersLbl and raffleData.winners then
            local winText = "Winners:\n"
            for _, w in ipairs(raffleData.winners) do
                winText = winText .. string.format("  #%d %s - %s gold (Ticket #%d)\n",
                    w.place, ColorText(w.name, "00FFCC"), FormatGold(w.prize), w.ticket)
            end
            self.overviewWinnersLbl:SetText(winText)
        end
    else
        if self.overviewPotLbl then self.overviewPotLbl:SetText("Pot: |c888888--|r") end
        if self.overviewTixLbl then self.overviewTixLbl:SetText("Tickets: |c888888--|r") end
        if self.overviewEntLbl then self.overviewEntLbl:SetText("Entrants: |c888888--|r") end
        if self.overviewPrizesLbl then self.overviewPrizesLbl:SetText("Prizes: No sealed ledger for this guild.") end
        if self.overviewWinnersLbl then self.overviewWinnersLbl:SetText("Winners: (Switch to Redfur Trading Post or Redfur Dealers)") end
    end
end

function FR:UpdateConsoleStatus()
    if not self.consoleStatusLbl then return end
    local saleCount = self:GetCount("sales")
    local depositCount = self:GetCount("deposits")
    self.consoleStatusLbl:SetText(string.format("%s | Sales: %s | Bank: %s | Guild: %d",
        ColorText("[ON] Connected", "59E08A"),
        ColorText(FormatGold(saleCount), "00FFCC"),
        ColorText(FormatGold(depositCount), "FFAA00"),
        self.selectedGuildIndex or 1))
end

--[[ =========================================================================
     TOGGLE & GLOBAL SLASH INTERACTION
========================================================================= ]]--

function FR:ToggleConsole(show)
    if not self.consoleWindow then
        self:CreateConsoleUI()
    end
    if not self.consoleWindow then return end

    if show == nil then
        show = self.consoleWindow:IsHidden()
    end

    if show then
        -- Suppress standalone legacy MotD window to prevent overlapping dialogs
        if self.motdWindow and not self.motdWindow:IsHidden() then
            self.motdWindow:SetHidden(true)
        end
        self:SelectConsoleGuild(self.selectedGuildIndex or 1)
        self:SelectConsoleTab(self.activeConsoleTab or 1)
        self:UpdateOverviewTab()
        self:UpdateConsoleStatus()
    end

    self.consoleWindow:SetHidden(not show)
end
