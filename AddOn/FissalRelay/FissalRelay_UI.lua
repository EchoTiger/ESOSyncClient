--[[
    FissalRelay_UI.lua
    Settings panel (LibAddonMenu-2.0), Floating Telemetry HUD Status Meter,
    and TTC Guild Bumper Interface
    Crafted by Echo & Fissal for Castle Echo and the Redfur Guilds.
]]--

FissalRelay = FissalRelay or {}
local FR = FissalRelay

-- Safe scene fragment attachment helpers
local function SafeAddFragment(scene, fragment)
    if scene and fragment then
        if not scene.HasFragment or not scene:HasFragment(fragment) then
            scene:AddFragment(fragment)
        end
    end
end

local function SafeRemoveFragment(scene, fragment)
    if scene and fragment then
        if not scene.HasFragment or scene:HasFragment(fragment) then
            scene:RemoveFragment(fragment)
        end
    end
end

-- Helper: Format seconds into a friendly ETA string
local function FormatETA(seconds)
    if not seconds or seconds < 0 then return "" end
    if seconds < 60 then
        return string.format("~%ds", math.max(1, math.floor(seconds)))
    elseif seconds < 3600 then
        local m = math.floor(seconds / 60)
        local s = math.floor(seconds % 60)
        return string.format("~%dm %ds", m, s)
    else
        local h = math.floor(seconds / 3600)
        local m = math.floor((seconds % 3600) / 60)
        return string.format("~%dh %dm", h, m)
    end
end

-- Helper: Query live LibHistoire cache link state and processor event queues
local function GetLibHistoireSyncStatus()
    local totalCategories = 0
    local linkedCategories = 0
    local requestingCategories = 0
    local totalPendingEvents = 0
    local maxTimeLeft = 0
    local totalSpeed = 0

    local numGuilds = GetNumGuilds()
    for i = 1, numGuilds do
        local guildId = GetGuildId(i)
        for _, cat in ipairs({ GUILD_HISTORY_EVENT_CATEGORY_TRADER, GUILD_HISTORY_EVENT_CATEGORY_BANKED_CURRENCY }) do
            local isBank = (cat == GUILD_HISTORY_EVENT_CATEGORY_BANKED_CURRENCY)
            local canTrack = not isBank or (FR.CanTrackGuildBank and FR:CanTrackGuildBank(guildId))

            if canTrack then
                totalCategories = totalCategories + 1
                if LibHistoire and LibHistoire.internal and LibHistoire.internal.historyCache then
                    local cache = LibHistoire.internal.historyCache:GetCategoryCache(guildId, cat)
                    if cache then
                        if cache.HasLinked and cache:HasLinked() then
                            linkedCategories = linkedCategories + 1
                        elseif cache.HasPendingRequest and cache:HasPendingRequest() then
                            requestingCategories = requestingCategories + 1
                        end
                    end
                end
            end
        end
    end

    for _, proc in pairs(FR.processors or {}) do
        if proc.GetPendingEventMetrics then
            local remaining, speed, timeLeft = proc:GetPendingEventMetrics()
            if remaining and remaining > 0 then
                totalPendingEvents = totalPendingEvents + remaining
                if speed and speed > 0 then totalSpeed = totalSpeed + speed end
                if timeLeft and timeLeft > maxTimeLeft then maxTimeLeft = timeLeft end
            end
        end
    end

    return linkedCategories, totalCategories, requestingCategories, totalPendingEvents, totalSpeed, maxTimeLeft
end

--[[ =========================================================================
     FLOATING STATUS METER HUD (Inspired by Clock & ESO Status Meters)
========================================================================= ]]--

function FR:CreateHUD()
    if self.hud then return end
    if not self.savedVars or not self.savedVars.settings then return end

    local wm = WINDOW_MANAGER

    -- 1. Main TopLevelWindow (draggable, mouse-interactive, clamped)
    local hud = wm:CreateTopLevelWindow("FissalRelay_HUD")
    hud:SetDimensions(350, 196)
    hud:SetClampedToScreen(true)
    hud:SetMouseEnabled(true)
    hud:SetMovable(true)

    -- Anchor / Position restoration
    local pos = self.savedVars.settings.hudPos
    hud:ClearAnchors()
    if pos and pos.x and pos.y and (pos.x ~= 0 or pos.y ~= 0) then
        hud:SetAnchor(TOPLEFT, GuiRoot, TOPLEFT, pos.x, pos.y)
    else
        hud:SetAnchor(TOPRIGHT, GuiRoot, TOPRIGHT, -45, 85)
    end

    hud:SetHandler("OnMoveStop", function(control)
        self.savedVars.settings.hudPos = {
            x = control:GetLeft(),
            y = control:GetTop(),
        }
    end)

    -- 2. Dark Tinted Backdrop
    local backdrop = wm:CreateControl("$(parent)_Backdrop", hud, CT_BACKDROP)
    backdrop:SetAnchorFill()
    backdrop:SetCenterColor(0.04, 0.04, 0.06, 0.85)
    backdrop:SetEdgeColor(0.20, 0.17, 0.12, 0.92)
    backdrop:SetEdgeTexture("", 8, 1, 0)

    -- 3. Clock Addon Status Meter Munge Texture
    local munge = wm:CreateControl("$(parent)_Munge", hud, CT_TEXTURE)
    munge:SetAnchorFill()
    munge:SetTexture("EsoUI/Art/Performance/StatusMeterMunge.dds")
    munge:SetAlpha(0.65)

    -- 4. Header Icon
    local icon = wm:CreateControl("$(parent)_Icon", hud, CT_TEXTURE)
    icon:SetAnchor(TOPLEFT, hud, TOPLEFT, 10, 8)
    icon:SetDimensions(20, 20)
    icon:SetTexture("EsoUI/Art/MainMenu/menuBar_guilds_up.dds")

    -- 5. Header Title
    local title = wm:CreateControl("$(parent)_Title", hud, CT_LABEL)
    title:SetAnchor(LEFT, icon, RIGHT, 6, 0)
    title:SetFont("ZoFontGameBold")
    title:SetText("|cFF9900FISSAL|r |c00FFCCRELAY|r")

    -- 6. Close Button [×]
    local closeBtn = wm:CreateControl("$(parent)_Close", hud, CT_BUTTON)
    closeBtn:SetAnchor(TOPRIGHT, hud, TOPRIGHT, -8, 6)
    closeBtn:SetDimensions(18, 18)
    closeBtn:SetFont("ZoFontGameBold")
    closeBtn:SetNormalFontColor(0.6, 0.6, 0.6, 1)
    closeBtn:SetMouseOverFontColor(1, 0.3, 0.3, 1)
    closeBtn:SetText("×")
    closeBtn:SetHandler("OnClicked", function()
        self:ToggleHUD(false)
    end)
    closeBtn:SetHandler("OnMouseEnter", function(ctrl)
        InitializeTooltip(InformationTooltip, ctrl, TOP, 0, -4)
        SetTooltipText(InformationTooltip, "Hide HUD (type /fissal ui to restore).")
    end)
    closeBtn:SetHandler("OnMouseExit", function()
        ClearTooltip(InformationTooltip)
    end)

    -- 7. Sync Button [Sync]
    local syncBtn = wm:CreateControl("$(parent)_Sync", hud, CT_BUTTON)
    syncBtn:SetAnchor(RIGHT, closeBtn, LEFT, -8, 0)
    syncBtn:SetDimensions(50, 18)
    syncBtn:SetFont("ZoFontGame")
    syncBtn:SetNormalFontColor(0, 1, 0.8, 1)
    syncBtn:SetMouseOverFontColor(1, 0.9, 0.4, 1)
    syncBtn:SetText("[Sync]")
    syncBtn:SetHandler("OnClicked", function()
        self:HandleSlashCommand("sync")
        self:UpdateHUD()
    end)
    syncBtn:SetHandler("OnMouseEnter", function(ctrl)
        InitializeTooltip(InformationTooltip, ctrl, TOP, 0, -4)
        SetTooltipText(InformationTooltip, "Turbo-pump LibHistoire history requests, scan kiosks, take fresh roster snapshots, and refresh telemetry.")
    end)
    syncBtn:SetHandler("OnMouseExit", function()
        ClearTooltip(InformationTooltip)
    end)

    -- 8. Header Divider Line
    local divider = wm:CreateControl("$(parent)_Div1", hud, CT_TEXTURE)
    divider:SetAnchor(TOPLEFT, hud, TOPLEFT, 8, 31)
    divider:SetAnchor(TOPRIGHT, hud, TOPRIGHT, -8, 31)
    divider:SetHeight(1)
    divider:SetColor(0.8, 0.5, 0.1, 0.4)

    -- 9. Telemetry Meter Rows (Enlarged with ZoFontGame & ZoFontGameBold)
    local function CreateMeterRow(name, labelText, yOffset)
        local lbl = wm:CreateControl("$(parent)_" .. name .. "_Lbl", hud, CT_LABEL)
        lbl:SetAnchor(TOPLEFT, hud, TOPLEFT, 12, yOffset)
        lbl:SetFont("ZoFontGame")
        lbl:SetColor(0.75, 0.75, 0.75, 1)
        lbl:SetText(labelText)

        local val = wm:CreateControl("$(parent)_" .. name .. "_Val", hud, CT_LABEL)
        val:SetAnchor(TOPRIGHT, hud, TOPRIGHT, -12, yOffset)
        val:SetFont("ZoFontGameBold")
        val:SetHorizontalAlignment(TEXT_ALIGN_RIGHT)
        val:SetText("--")

        return val
    end

    self.hudElements = {
        salesVal  = CreateMeterRow("Sales",   "Sales Ingest:",     36),
        syncVal   = CreateMeterRow("Sync",    "History Sync:",     59),
        kiosksVal = CreateMeterRow("Kiosks",  "Kiosks & Bids:",    82),
        rosterVal = CreateMeterRow("Rosters", "Roster & Dues:",    105),
        readyVal  = CreateMeterRow("Ready",   "Upload Status:",    128),
    }

    local readyLbl = wm:GetControlByName("FissalRelay_HUD_Ready_Lbl")
    if readyLbl then
        readyLbl:SetMouseEnabled(true)
        readyLbl:SetHandler("OnMouseEnter", function(ctrl)
            InitializeTooltip(InformationTooltip, ctrl, TOP, 0, -4)
            SetTooltipText(InformationTooltip, "Unpruned records staged in SavedVariables ready for Fissal Relay sync.\nS = Sales\nD = Bank Deposits")
        end)
        readyLbl:SetHandler("OnMouseExit", function()
            ClearTooltip(InformationTooltip)
        end)
    end

    -- 10. Bottom Divider Line
    local divider2 = wm:CreateControl("$(parent)_Div2", hud, CT_TEXTURE)
    divider2:SetAnchor(TOPLEFT, hud, TOPLEFT, 8, 154)
    divider2:SetAnchor(TOPRIGHT, hud, TOPRIGHT, -8, 154)
    divider2:SetHeight(1)
    divider2:SetColor(0.3, 0.3, 0.35, 0.4)

    -- 11. Footer Heartbeat & Status Indicator
    local footer = wm:CreateControl("$(parent)_Footer", hud, CT_LABEL)
    footer:SetAnchor(TOPLEFT, hud, TOPLEFT, 12, 162)
    footer:SetAnchor(TOPRIGHT, hud, TOPRIGHT, -12, 162)
    footer:SetFont("ZoFontGameSmall")
    footer:SetText("|c00FF00●|r Courier Ready • LibHistoire: Auto • /fissal")
    self.hudElements.footer = footer

    -- 12. Scene Fragment Setup (Fades during menus / loading screens)
    self.hud = hud
    self.hudFragment = ZO_HUDFadeSceneFragment:New(hud)

    if self.savedVars.settings.showHud then
        SafeAddFragment(HUD_SCENE, self.hudFragment)
        SafeAddFragment(HUD_UI_SCENE, self.hudFragment)
        hud:SetHidden(false)
    else
        hud:SetHidden(true)
    end

    -- 13. Throttled Periodic 1-Second Refresh
    local lastTick = 0
    hud:SetHandler("OnUpdate", function(_, currentTime)
        if currentTime - lastTick >= 1.0 then
            lastTick = currentTime
            FR:UpdateHUD()
        end
    end)

    self:UpdateHUD()
end

function FR:UpdateHUD()
    if not self.hud or not self.hudElements or self.hud:IsHidden() then return end

    -- 1. Sales Count and Accumulated Value
    local saleCount = NonContiguousCount(self.savedVars.sales or {})
    local totalGold = 0
    for _, s in pairs(self.savedVars.sales or {}) do
        totalGold = totalGold + (s.price or 0)
    end
    local goldStr = ""
    if totalGold >= 1000000 then
        goldStr = string.format(" (|cFFD700%.1fM|r)", totalGold / 1000000)
    elseif totalGold >= 1000 then
        goldStr = string.format(" (|cFFD700%.1fk|r)", totalGold / 1000)
    elseif totalGold > 0 then
        goldStr = string.format(" (|cFFD700%d|r)", totalGold)
    end
    self.hudElements.salesVal:SetText(string.format("|c00FFCC%s|r%s", ZO_LocalizeDecimalNumber(saleCount), goldStr))

    -- 2. LibHistoire Sync Status: x-of-x Channels & Queue ETA
    local linked, totalCats, requesting, pendingEvents, speed, timeLeft = GetLibHistoireSyncStatus()

    if pendingEvents > 0 then
        local etaStr = FormatETA(timeLeft)
        local speedStr = (speed and speed > 0) and string.format(" (%d/s)", speed) or ""
        self.hudElements.syncVal:SetText(string.format("|cFFCC00%s queued %s%s|r",
            ZO_LocalizeDecimalNumber(pendingEvents), etaStr, speedStr))
    elseif requesting > 0 then
        self.hudElements.syncVal:SetText(string.format("|cFF9900%d of %d Linked (Turbo Fetch)|r", linked, totalCats))
    elseif linked > 0 and linked == totalCats then
        self.hudElements.syncVal:SetText(string.format("|c00FF00All %d of %d Synced (100%%)|r", linked, totalCats))
    elseif linked > 0 then
        self.hudElements.syncVal:SetText(string.format("|c00FFCC%d of %d Linked (Green)|r", linked, totalCats))
    else
        self.hudElements.syncVal:SetText("|c888888Connecting to History...|r")
    end

    -- 3. Kiosks & Bids Ledger
    local scoutCount = NonContiguousCount(self.savedVars.kiosks or {})
    local ownedCount = 0
    local numGuilds = GetNumGuilds()
    for i = 1, numGuilds do
        local gId = GetGuildId(i)
        if GetGuildOwnedKioskInfo and GetGuildOwnedKioskInfo(gId) then
            ownedCount = ownedCount + 1
        end
    end
    local bidCount = NonContiguousCount(self.savedVars.staff and self.savedVars.staff.bids or {})
    self.hudElements.kiosksVal:SetText(string.format("|c00FF00%d|r owned • |cFFD700%d|r bids", ownedCount, bidCount))

    -- 4. Rosters & Bank Dues
    local rosters = NonContiguousCount(self.savedVars.staff and self.savedVars.staff.rosterSnapshots or {})
    local deposits = NonContiguousCount(self.savedVars.staff and self.savedVars.staff.bankDeposits or {})
    self.hudElements.rosterVal:SetText(string.format("|c00FFFF%d|r guilds • |cFFAA00%s|r deps",
        rosters, ZO_LocalizeDecimalNumber(deposits)))

    -- 5. Upload Readiness & Telemetry Buffer Status
    if self.hudElements.readyVal then
        local totalStaged = saleCount + deposits
        if totalStaged > 0 then
            self.hudElements.readyVal:SetText(string.format("|c00FF00● Ready|r (|c00FFCC%s|r S • |cFFAA00%s|r D)",
                ZO_LocalizeDecimalNumber(saleCount), ZO_LocalizeDecimalNumber(deposits)))
        else
            self.hudElements.readyVal:SetText("|c888888○ Idle (0 staged)|r")
        end
    end

    -- 6. Heartbeat & Listener Health
    local isListening = self.isReady and (NonContiguousCount(self.processors or {}) > 0)
    if pendingEvents > 0 then
        local etaStr = FormatETA(timeLeft)
        self.hudElements.footer:SetText(string.format("|cFFCC00●|r Catching Up %s • /fissal", etaStr))
    else
        local statusDot = isListening and "|c00FF00●|r" or "|cFF9900●|r"
        local statusText = isListening and "Courier Ready" or "Waiting on History"
        self.hudElements.footer:SetText(string.format("%s %s • LibHistoire: Auto • /fissal", statusDot, statusText))
    end
end

function FR:ToggleHUD(show)
    if show == nil then
        show = not self.savedVars.settings.showHud
    end
    self.savedVars.settings.showHud = show

    if not self.hud then
        self:CreateHUD()
    end

    if self.hud then
        if show then
            SafeAddFragment(HUD_SCENE, self.hudFragment)
            SafeAddFragment(HUD_UI_SCENE, self.hudFragment)
            self.hud:SetHidden(false)
            self:UpdateHUD()
            self.PrintChat("Status HUD |c00FF00shown|r. Drag anywhere to reposition.")
        else
            SafeRemoveFragment(HUD_SCENE, self.hudFragment)
            SafeRemoveFragment(HUD_UI_SCENE, self.hudFragment)
            self.hud:SetHidden(true)
            self.PrintChat("Status HUD |cFF5555hidden|r. (Type /fissal ui to restore)")
        end
    end
end

function FR:ResetHUDPosition()
    if not self.hud then
        self:CreateHUD()
    end

    if self.hud then
        self.hud:ClearAnchors()
        self.hud:SetAnchor(TOPRIGHT, GuiRoot, TOPRIGHT, -45, 85)
        self.savedVars.settings.hudPos = {
            x = self.hud:GetLeft(),
            y = self.hud:GetTop(),
        }
        self.PrintChat("Status HUD position reset to top right.")
        self:UpdateHUD()
    end
end

--[[ =========================================================================
     TTC GUILD BUMPER UI (At the Guild Store)
========================================================================= ]]--

function FR:CreateBumperUI()
    if self.bumperWindow then return end
    if not self.savedVars or not self.savedVars.settings then return end

    local wm = WINDOW_MANAGER

    -- 1. Main TopLevelWindow (Draggable & clamped)
    local bumper = wm:CreateTopLevelWindow("FissalRelay_Bumper")
    bumper:SetDimensions(320, 240)
    bumper:SetClampedToScreen(true)
    bumper:SetMouseEnabled(true)
    bumper:SetMovable(true)

    -- Initially hidden so it doesn't pop up on login/world entry
    bumper:SetHidden(true)

    -- Anchor: Default to left of Guild Store (ZO_TradingHouse)
    local pos = self.savedVars.settings.bumperPos
    bumper:ClearAnchors()
    if pos and pos.x and pos.y and (pos.x ~= 0 or pos.y ~= 0) then
        bumper:SetAnchor(TOPLEFT, GuiRoot, TOPLEFT, pos.x, pos.y)
    elseif ZO_TradingHouse then
        bumper:SetAnchor(TOPRIGHT, ZO_TradingHouse, TOPLEFT, -15, 60)
    else
        bumper:SetAnchor(TOPLEFT, GuiRoot, TOPLEFT, 80, 120)
    end

    bumper:SetHandler("OnMoveStop", function(control)
        self.savedVars.settings.bumperPos = {
            x = control:GetLeft(),
            y = control:GetTop(),
        }
    end)

    -- 2. Dark Tinted Backdrop
    local backdrop = wm:CreateControl("$(parent)_Backdrop", bumper, CT_BACKDROP)
    backdrop:SetAnchorFill()
    backdrop:SetCenterColor(0.04, 0.04, 0.06, 0.88)
    backdrop:SetEdgeColor(0.20, 0.17, 0.12, 0.92)
    backdrop:SetEdgeTexture("", 8, 1, 0)

    -- 3. Munge Bezel Texture
    local munge = wm:CreateControl("$(parent)_Munge", bumper, CT_TEXTURE)
    munge:SetAnchorFill()
    munge:SetTexture("EsoUI/Art/Performance/StatusMeterMunge.dds")
    munge:SetAlpha(0.65)

    -- 4. Header Icon
    local icon = wm:CreateControl("$(parent)_Icon", bumper, CT_TEXTURE)
    icon:SetAnchor(TOPLEFT, bumper, TOPLEFT, 10, 8)
    icon:SetDimensions(20, 20)
    icon:SetTexture("EsoUI/Art/MainMenu/menuBar_guilds_up.dds")

    -- 5. Title
    local title = wm:CreateControl("$(parent)_Title", bumper, CT_LABEL)
    title:SetAnchor(LEFT, icon, RIGHT, 6, 0)
    title:SetFont("ZoFontGameBold")
    title:SetText("|cFF9900FISSAL|r |c00FFCCTTC BUMPER|r")

    -- 6. Close Button [×]
    local closeBtn = wm:CreateControl("$(parent)_Close", bumper, CT_BUTTON)
    closeBtn:SetAnchor(TOPRIGHT, bumper, TOPRIGHT, -8, 6)
    closeBtn:SetDimensions(18, 18)
    closeBtn:SetFont("ZoFontGameBold")
    closeBtn:SetNormalFontColor(0.6, 0.6, 0.6, 1)
    closeBtn:SetMouseOverFontColor(1, 0.3, 0.3, 1)
    closeBtn:SetText("×")
    closeBtn:SetHandler("OnClicked", function()
        self:ToggleBumperUI(false)
    end)

    -- 7. Header Divider Line
    local divider = wm:CreateControl("$(parent)_Div1", bumper, CT_TEXTURE)
    divider:SetAnchor(TOPLEFT, bumper, TOPLEFT, 8, 31)
    divider:SetAnchor(TOPRIGHT, bumper, TOPRIGHT, -8, 31)
    divider:SetHeight(1)
    divider:SetColor(0.8, 0.5, 0.1, 0.4)

    -- 8. Subtitle
    local subtitle = wm:CreateControl("$(parent)_Subtitle", bumper, CT_LABEL)
    subtitle:SetAnchor(TOPLEFT, bumper, TOPLEFT, 12, 34)
    subtitle:SetFont("ZoFontGameSmall")
    subtitle:SetColor(0.8, 0.8, 0.8, 1)
    subtitle:SetText("Select guilds to bump to TamrielTradeCentre:")

    -- 9. Guild Checkbox Rows (up to 5 guilds)
    self.bumperGuildRows = {}
    local startY = 52
    for i = 1, 5 do
        local rowY = startY + (i - 1) * 23

        local row = wm:CreateControl("$(parent)_GuildRow_" .. i, bumper, CT_CONTROL)
        row:SetAnchor(TOPLEFT, bumper, TOPLEFT, 10, rowY)
        row:SetDimensions(300, 22)
        row:SetMouseEnabled(true)

        local checkBtn = wm:CreateControl("$(parent)_Check", row, CT_BUTTON)
        checkBtn:SetAnchor(LEFT, row, LEFT, 2, 0)
        checkBtn:SetDimensions(20, 20)
        checkBtn:SetFont("ZoFontGameBold")
        checkBtn:SetText("[ ]")

        local nameLbl = wm:CreateControl("$(parent)_Name", row, CT_LABEL)
        nameLbl:SetAnchor(LEFT, checkBtn, RIGHT, 6, 0)
        nameLbl:SetFont("ZoFontGame")
        nameLbl:SetText("--")

        local kioskBadge = wm:CreateControl("$(parent)_Kiosk", row, CT_LABEL)
        kioskBadge:SetAnchor(RIGHT, row, RIGHT, -4, 0)
        kioskBadge:SetFont("ZoFontGameSmall")
        kioskBadge:SetHorizontalAlignment(TEXT_ALIGN_RIGHT)
        kioskBadge:SetText("")

        self.bumperGuildRows[i] = {
            control = row,
            checkBtn = checkBtn,
            nameLbl = nameLbl,
            kioskBadge = kioskBadge,
            guildId = nil,
        }
    end

    -- 10. Bottom Divider Line
    local divider2 = wm:CreateControl("$(parent)_Div2", bumper, CT_TEXTURE)
    divider2:SetAnchor(TOPLEFT, bumper, TOPLEFT, 8, 172)
    divider2:SetAnchor(TOPRIGHT, bumper, TOPRIGHT, -8, 172)
    divider2:SetHeight(1)
    divider2:SetColor(0.3, 0.3, 0.35, 0.4)

    -- 11. Status / Progress Label
    local statusLbl = wm:CreateControl("$(parent)_Status", bumper, CT_LABEL)
    statusLbl:SetAnchor(TOPLEFT, bumper, TOPLEFT, 12, 177)
    statusLbl:SetAnchor(TOPRIGHT, bumper, TOPRIGHT, -12, 177)
    statusLbl:SetFont("ZoFontGameSmall")
    statusLbl:SetText("Ready to bump.")
    self.bumperStatusText = statusLbl

    -- 12. Bump Action Button [⚡ Bump Selected Guilds]
    local bumpBtn = wm:CreateControl("$(parent)_BumpBtn", bumper, CT_BUTTON)
    bumpBtn:SetAnchor(BOTTOMLEFT, bumper, BOTTOMLEFT, 12, -8)
    bumpBtn:SetDimensions(205, 24)
    bumpBtn:SetFont("ZoFontGameBold")
    bumpBtn:SetNormalFontColor(0, 1, 0.8, 1)
    bumpBtn:SetMouseOverFontColor(1, 0.9, 0.4, 1)
    bumpBtn:SetText("⚡ Bump Selected")
    bumpBtn:SetHandler("OnClicked", function()
        if self.isBumping then
            self:CancelBump()
        else
            self:StartBump()
        end
    end)
    self.bumperActionBtn = bumpBtn

    -- 13. ReloadUI Button
    local reloadBtn = wm:CreateControl("$(parent)_ReloadBtn", bumper, CT_BUTTON)
    reloadBtn:SetAnchor(LEFT, bumpBtn, RIGHT, 8, 0)
    reloadBtn:SetDimensions(80, 24)
    reloadBtn:SetFont("ZoFontGameSmall")
    reloadBtn:SetNormalFontColor(1, 0.8, 0.2, 1)
    reloadBtn:SetMouseOverFontColor(1, 1, 1, 1)
    reloadBtn:SetText("ReloadUI")
    reloadBtn:SetHandler("OnClicked", function()
        ReloadUI()
    end)
    self.bumperReloadBtn = reloadBtn

    -- 14. Guild Store Scene Integration (Only visible when store opens, or toggled)
    self.bumperWindow = bumper

    if TRADING_HOUSE_SCENE then
        TRADING_HOUSE_SCENE:RegisterCallback("StateChange", function(oldState, newState)
            if newState == SCENE_SHOWING then
                if self.savedVars.settings.showBumper then
                    local curPos = self.savedVars.settings.bumperPos
                    if not (curPos and (curPos.x ~= 0 or curPos.y ~= 0)) and ZO_TradingHouse then
                        self.bumperWindow:ClearAnchors()
                        self.bumperWindow:SetAnchor(TOPRIGHT, ZO_TradingHouse, TOPLEFT, -15, 60)
                    end
                    self.bumperWindow:SetHidden(false)
                    self:UpdateBumperUI()
                end
            elseif newState == SCENE_HIDING or newState == SCENE_HIDDEN then
                if not self.isBumping then
                    self.bumperWindow:SetHidden(true)
                end
            end
        end)
    end

    self:UpdateBumperUI()
end

function FR:UpdateBumperUI(customStatus)
    if not self.bumperWindow or not self.bumperGuildRows then return end

    local numGuilds = GetNumGuilds()

    for i = 1, 5 do
        local rowData = self.bumperGuildRows[i]
        if i <= numGuilds then
            local gId = GetGuildId(i)
            local gName = GetGuildName(gId)
            local kioskName = GetGuildOwnedKioskInfo and GetGuildOwnedKioskInfo(gId)

            rowData.guildId = gId
            rowData.control:SetHidden(false)
            rowData.nameLbl:SetText(gName)

            local isSelected = self:IsGuildBumpSelected(gId)
            if isSelected then
                rowData.checkBtn:SetText("|cFFD700[✓]|r")
            else
                rowData.checkBtn:SetText("|c555555[ ]|r")
            end

            local function ToggleRow()
                if self.isBumping then return end
                local newVal = not self:IsGuildBumpSelected(gId)
                self:SetGuildBumpSelected(gId, newVal)
                self:UpdateBumperUI()
            end

            rowData.checkBtn:SetHandler("OnClicked", ToggleRow)
            rowData.control:SetHandler("OnMouseUp", ToggleRow)

            if kioskName and kioskName ~= "" then
                local location = kioskName:match(" in (.*)") or kioskName
                rowData.kioskBadge:SetText(string.format("|c00FF00[%s]|r", location))
            else
                rowData.kioskBadge:SetText("|c666666[No Kiosk]|r")
            end
        else
            rowData.control:SetHidden(true)
            rowData.guildId = nil
        end
    end

    if self.bumperActionBtn then
        if self.isBumping then
            self.bumperActionBtn:SetText("|cFF5555■ Cancel Bump|r")
            self.bumperActionBtn:SetNormalFontColor(1, 0.3, 0.3, 1)
        else
            self.bumperActionBtn:SetText("|c00FFCC⚡ Bump Selected|r")
            self.bumperActionBtn:SetNormalFontColor(0, 1, 0.8, 1)
        end
    end

    if self.bumperStatusText then
        if customStatus then
            self.bumperStatusText:SetText(customStatus)
        elseif self.isBumping then
            local entry = self.bumpQueue and self.bumpQueue[self.currentBumpIndex]
            local name = entry and entry.name or "Guild"
            self.bumperStatusText:SetText(string.format("|cFFCC00Bumping %s...|r", name))
        else
            local count = 0
            for i = 1, numGuilds do
                if self:IsGuildBumpSelected(GetGuildId(i)) then count = count + 1 end
            end
            self.bumperStatusText:SetText(string.format("Ready to bump |c00FFCC%d|r guild(s).", count))
        end
    end
end

function FR:ToggleBumperUI(show)
    if not self.bumperWindow then
        self:CreateBumperUI()
    end

    if show == nil then
        show = self.bumperWindow:IsHidden()
    end

    if self.bumperWindow then
        self.bumperWindow:SetHidden(not show)
        if show then
            self:UpdateBumperUI()
        end
    end
end

function FR:ResetBumperPosition()
    if not self.bumperWindow then
        self:CreateBumperUI()
    end

    if self.bumperWindow then
        self.bumperWindow:ClearAnchors()
        if ZO_TradingHouse then
            self.bumperWindow:SetAnchor(TOPRIGHT, ZO_TradingHouse, TOPLEFT, -15, 60)
        else
            self.bumperWindow:SetAnchor(TOPLEFT, GuiRoot, TOPLEFT, 80, 120)
        end
        self.savedVars.settings.bumperPos = { x = 0, y = 0 }
        self.PrintChat("Bumper position reset to default (docked to Guild Store).")
    end
end

--[[ =========================================================================
     LIBADDONMENU-2.0 SETTINGS PANEL
========================================================================= ]]--

function FR:CreateSettingsMenu()
    local LAM = LibAddonMenu2
    if not LAM then return end

    local panelData = {
        type = "panel",
        name = "Fissal's Cogwork Relay",
        displayName = "|cFF9900Fissal's|r Cogwork Relay",
        author = "Echo & Fissal",
        version = FR.version,
        registerForRefresh = true,
        registerForDefaults = true,
    }

    local optionsData = {
        {
            type = "header",
            name = "Courier Options",
        },
        {
            type = "description",
            text = "Fissal watches your guild store transactions, kiosk ground recon, and bank ledgers with clockwork precision, feeding data smoothly to Castle Echo.",
        },
        {
            type = "checkbox",
            name = "Chat Announcements",
            tooltip = "Display Fissal status notifications and purrs in the chat window.",
            getFunc = function() return FR.savedVars.settings.chatAnnouncements end,
            setFunc = function(value) FR.savedVars.settings.chatAnnouncements = value end,
            default = true,
        },
        {
            type = "checkbox",
            name = "Announce Kiosk Ground Recon",
            tooltip = "Print a notification whenever you open a guild trader kiosk with its confirmed hiring guild.",
            getFunc = function() return FR.savedVars.settings.announceKioskRecon end,
            setFunc = function(value) FR.savedVars.settings.announceKioskRecon = value end,
            default = true,
        },
        {
            type = "checkbox",
            name = "Mechanical Sound Effects",
            tooltip = "Play soft brass lockpicking cues when Fissal executes commands.",
            getFunc = function() return FR.savedVars.settings.soundEffects end,
            setFunc = function(value) FR.savedVars.settings.soundEffects = value end,
            default = true,
        },
        {
            type = "slider",
            name = "History Depth (Days)",
            tooltip = "How many days of guild history to harvest if starting fresh.",
            min = 7,
            max = 60,
            step = 1,
            getFunc = function() return FR.savedVars.historyDepthDays end,
            setFunc = function(value) FR.savedVars.historyDepthDays = value end,
            default = 30,
        },
        {
            type = "button",
            name = "Prune Old Data Now",
            tooltip = "Immediately purge local sales records older than your configured History Depth.",
            func = function()
                local pruned = FR:PruneExpiredData(true)
                local msg
                if pruned and pruned > 0 then
                    msg = string.format("Pruning complete! Purged %s expired sales records (> %d days old).",
                        ZO_LocalizeDecimalNumber(pruned), FR.savedVars.historyDepthDays or 30)
                else
                    msg = string.format("All records are fresh! No sales older than %d days found.",
                        FR.savedVars.historyDepthDays or 30)
                end
                ZO_Alert(UI_ALERT_CATEGORY_ALERT, SOUNDS.LOCKPICKING_UNLOCKED, "|cFF9900[Fissal]|r " .. msg)
            end,
            isDangerous = true,
            warning = "This will immediately remove stored sales older than your configured History Depth from SavedVariables.",
            width = "full",
        },
        {
            type = "header",
            name = "Fissal HUD Status Meter",
        },
        {
            type = "description",
            text = "A sleek, floating in-game telemetry widget showing real-time sales ingest, LibHistoire sync progress, bids, and roster sync status.",
        },
        {
            type = "checkbox",
            name = "Show Floating Status HUD",
            tooltip = "Display the floating Fissal status meter widget on screen. You can also toggle it anytime using /fissal ui or /fissal hud.",
            getFunc = function() return FR.savedVars.settings.showHud end,
            setFunc = function(value) FR:ToggleHUD(value) end,
            default = true,
        },
        {
            type = "button",
            name = "Reset HUD Position",
            tooltip = "Reset the HUD widget back to its default position on the top-right of your screen.",
            func = function()
                FR:ResetHUDPosition()
            end,
            width = "half",
        },
        {
            type = "button",
            name = "Toggle HUD (Show/Hide)",
            tooltip = "Quickly toggle the HUD on or off.",
            func = function()
                FR:ToggleHUD()
            end,
            width = "half",
        },
        {
            type = "header",
            name = "TTC Guild Bumper",
        },
        {
            type = "description",
            text = "Automated multi-guild scanning to push fresh listings to TamrielTradeCentre so your sales are seen first.",
        },
        {
            type = "checkbox",
            name = "Auto-Show Bumper at Guild Store",
            tooltip = "Automatically display the Bumper selection panel whenever you open the Guild Store.",
            getFunc = function() return FR.savedVars.settings.showBumper end,
            setFunc = function(value)
                FR.savedVars.settings.showBumper = value
                if not value and FR.bumperWindow then FR.bumperWindow:SetHidden(true) end
            end,
            default = true,
        },
        {
            type = "button",
            name = "Reset Bumper Position",
            tooltip = "Reset the Bumper window position back to default.",
            func = function()
                FR:ResetBumperPosition()
            end,
            width = "half",
        },
        {
            type = "button",
            name = "Trigger TTC Bump Now",
            tooltip = "Start scanning selected guilds right now (requires open guild store).",
            func = function()
                FR:StartBump()
            end,
            width = "half",
        },
        {
            type = "header",
            name = "Ground Recon & Scouting",
        },
        {
            type = "description",
            text = "Visiting any Guild Trader in Tamriel automatically records the holding guild and coordinates, grounding Castle Echo with authoritative in-game truth.",
        },
        {
            type = "button",
            name = "View Scouted Kiosks",
            tooltip = "Display all in-person verified kiosks in chat.",
            func = function()
                FR:HandleSlashCommand("scout")
            end,
            width = "half",
        },
        {
            type = "button",
            name = "Check Relay Status",
            tooltip = "Inspect current sales tally, kiosks scouted, and active listeners.",
            func = function()
                FR:HandleSlashCommand("status")
            end,
            width = "half",
        },
        {
            type = "button",
            name = "View Kiosk Bids Ledger",
            tooltip = "Display all recorded bids, winning stalls, and refunds in chat.",
            func = function()
                FR:HandleSlashCommand("bids")
            end,
            width = "full",
        },
        {
            type = "header",
            name = "Staff Management Tools",
        },
        {
            type = "checkbox",
            name = "Auto Roster Snapshot on Login",
            tooltip = "Automatically snapshot guild rosters 10 seconds after logging in.",
            getFunc = function() return FR.savedVars.settings.autoRosterSnapshotOnLogin end,
            setFunc = function(value) FR.savedVars.settings.autoRosterSnapshotOnLogin = value end,
            default = true,
        },
        {
            type = "button",
            name = "Audit Inactives (Guild 1, 14d)",
            tooltip = "Scan members inactive > 14 days and print summary for Discord purge.",
            func = function()
                FR:AuditInactives(1, 14)
            end,
            width = "half",
        },
        {
            type = "button",
            name = "Audit Bank Dues (Guild 1, 7d)",
            tooltip = "Aggregate weekly gold deposits for raffle tickets and dues.",
            func = function()
                FR:AuditBankDues(1, 7)
            end,
            width = "half",
        },
        {
            type = "button",
            name = "Snapshot Rosters Now",
            tooltip = "Capture current member list, ranks, and notes for all guilds.",
            func = function()
                local count = FR:TakeRosterSnapshot()
                d(string.format("|cFF9900[Fissal]|r Snapped roster for %d guild(s).", count))
            end,
            width = "full",
        },
        {
            type = "header",
            name = "Guild Bank Deposit Tracking",
        },
        {
            type = "description",
            text = "Fissal verifies rank permissions before requesting guild bank deposits and bids. Guilds where you do not have permission to view bank gold are automatically skipped to prevent request stalls and queue lockouts.",
        },
    }

    local numGuilds = GetNumGuilds()
    for i = 1, numGuilds do
        local guildId = GetGuildId(i)
        local guildName = GetGuildName(guildId)
        local hasPrivilege = DoesGuildHavePrivilege and DoesGuildHavePrivilege(guildId, GUILD_PRIVILEGE_BANK_DEPOSIT)
        local hasPermission = DoesPlayerHaveGuildPermission and GUILD_PERMISSION_BANK_VIEW_GOLD and DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_BANK_VIEW_GOLD)
        local isGM = IsPlayerGuildMaster and IsPlayerGuildMaster(guildId)

        local permStatus
        if isGM then
            permStatus = "|c00FF00(Guild Master)|r"
        elseif hasPermission then
            permStatus = "|c00FF00(Permission Granted)|r"
        elseif not hasPrivilege then
            permStatus = "|c888888(No Guild Bank)|r"
        else
            permStatus = "|cFF5555(No Permission - Skipped)|r"
        end

        table.insert(optionsData, {
            type = "checkbox",
            name = string.format("%s %s", guildName, permStatus),
            tooltip = string.format("Enable or disable bank deposit tracking for %s. When enabled, Fissal requires 'View Guild Bank Gold' permission.", guildName),
            getFunc = function()
                if FR.savedVars and FR.savedVars.settings and FR.savedVars.settings.bankGuilds then
                    local val = FR.savedVars.settings.bankGuilds[guildId]
                    if val ~= nil then return val end
                end
                return (isGM or hasPermission) and true or false
            end,
            setFunc = function(value)
                if not FR.savedVars.settings.bankGuilds then
                    FR.savedVars.settings.bankGuilds = {}
                end
                FR.savedVars.settings.bankGuilds[guildId] = value
                if FR.SetupProcessors then FR:SetupProcessors() end
                if FR.UpdateHUD then FR:UpdateHUD() end
            end,
            default = (isGM or hasPermission) and true or false,
        })
    end

    LAM:RegisterAddonPanel("FissalRelay_Options", panelData)
    LAM:RegisterOptionControls("FissalRelay_Options", optionsData)
end

--[[ =========================================================================
     INITIALIZATION
========================================================================= ]]--

local function OnPlayerActivated()
    EVENT_MANAGER:UnregisterForEvent("FissalRelay_UI", EVENT_PLAYER_ACTIVATED)
    FR:CreateSettingsMenu()
    FR:CreateHUD()
    FR:CreateBumperUI()
end

EVENT_MANAGER:RegisterForEvent("FissalRelay_UI", EVENT_PLAYER_ACTIVATED, OnPlayerActivated)
