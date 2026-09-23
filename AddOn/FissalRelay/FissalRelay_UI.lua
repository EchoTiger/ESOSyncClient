--[[
    FissalRelay_UI.lua
    Settings panel (LibAddonMenu-2.0), Floating Telemetry HUD Status Meter,
    and TTC Guild Bumper Interface
    Crafted by Echo & Fissal for Fissal Relay and the Redfur Guilds.
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
            local canTrack = FR.CanTrackCategory and FR:CanTrackCategory(guildId, cat)

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
    hud:SetDimensions(350, 220)
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
    backdrop:SetEdgeColor(0.75, 0.50, 0.10, 0.95)  -- H1: stronger amber edge for bright env contrast
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
        val:SetWidth(205)
        val:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)

        return lbl, val
    end

    local _salesLbl,  _salesVal  = CreateMeterRow("Sales",   "Sales Ingest:",     36)
    local _syncLbl,   _syncVal   = CreateMeterRow("Sync",    "History Sync:",     59)
    local _kiosksLbl, _kiosksVal = CreateMeterRow("Kiosks",  "Kiosks & Bids:",    82)
    local _rosterLbl, _rosterVal = CreateMeterRow("Rosters", "Roster & Dues:",    105)
    local _readyLbl,  _readyVal  = CreateMeterRow("Ready",   "Upload Status:",    128)

    self.hudElements = {
        salesVal  = _salesVal,
        syncVal   = _syncVal,
        kiosksVal = _kiosksVal,
        rosterVal = _rosterVal,
        readyVal  = _readyVal,
    }

    local readyLbl = _readyLbl
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

    -- History Sync Channel Tooltip Breakdown
    local syncLbl = _syncLbl
    local syncVal = _syncVal

    local function ShowSyncTooltip(ctrl)
        InitializeTooltip(InformationTooltip, ctrl, TOP, 0, -4)
        local details = FR.GetLibHistoireChannelDetails and FR:GetLibHistoireChannelDetails() or {}
        local lines = { "|cFF9900History Sync Telemetry|r" }
        if #details == 0 then
            table.insert(lines, "|c888888Connecting to LibHistoire cache...|r")
        else
            for _, d in ipairs(details) do
                local tStatus
                if not d.trader.canTrack then
                    tStatus = "|c666666[Excluded - No Trader]|r"
                elseif d.trader.linked then
                    tStatus = "|c00FF00[Linked]|r"
                elseif d.trader.pending then
                    tStatus = "|cFF9900[Fetching...]|r"
                else
                    tStatus = "|cFFCC00[Unlinked]|r"
                end
                local bStatus
                if not d.bank.canTrack then
                    bStatus = "|c666666[Excluded - No Perms]|r"
                elseif d.bank.linked then
                    bStatus = "|c00FF00[Linked]|r"
                elseif d.bank.pending then
                    bStatus = "|cFF9900[Fetching...]|r"
                else
                    bStatus = "|cFFCC00[Unlinked]|r"
                end
                table.insert(lines, string.format("|cFFFFFF%s|r\n  Sales: %s  •  Bank: %s", d.guildName, tStatus, bStatus))
            end
        end
        SetTooltipText(InformationTooltip, table.concat(lines, "\n"))
    end

    if syncLbl then
        syncLbl:SetMouseEnabled(true)
        syncLbl:SetHandler("OnMouseEnter", ShowSyncTooltip)
        syncLbl:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)
    end
    if syncVal then
        syncVal:SetMouseEnabled(true)
        syncVal:SetHandler("OnMouseEnter", ShowSyncTooltip)
        syncVal:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)
    end

    -- 10. Last Bump Meter Row (6th telemetry row)
    local _bumpLbl, _bumpVal = CreateMeterRow("Bump", "Last Bump:", 151)
    self.hudElements.bumpVal = _bumpVal

    -- 11. Bottom Divider Line
    local divider2 = wm:CreateControl("$(parent)_Div2", hud, CT_TEXTURE)
    divider2:SetAnchor(TOPLEFT, hud, TOPLEFT, 8, 177)
    divider2:SetAnchor(TOPRIGHT, hud, TOPRIGHT, -8, 177)
    divider2:SetHeight(1)
    divider2:SetColor(0.3, 0.3, 0.35, 0.4)

    -- 12. Footer Heartbeat & Status Indicator
    local footer = wm:CreateControl("$(parent)_Footer", hud, CT_LABEL)
    footer:SetAnchor(TOPLEFT, hud, TOPLEFT, 12, 185)
    footer:SetAnchor(TOPRIGHT, hud, TOPRIGHT, -12, 185)
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

    -- 1. Sales Count and Accumulated Value (O(1) lookup)
    local saleCount = self:GetCount("sales")
    local totalGold = self.savedVars and self.savedVars.totalSalesGold or 0
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

    -- 3b. Last Bump Time
    if self.hudElements.bumpVal then
        local lastBump = self.savedVars.lastBumpTime or 0
        if lastBump > 0 then
            local ago = GetTimeStamp() - lastBump
            local agoStr
            if ago < 60 then agoStr = "just now"
            elseif ago < 3600 then agoStr = string.format("%dm ago", math.floor(ago / 60))
            elseif ago < 86400 then agoStr = string.format("%dh ago", math.floor(ago / 3600))
            else agoStr = string.format("%dd ago", math.floor(ago / 86400))
            end
            self.hudElements.bumpVal:SetText(string.format("|c00FFCC%s|r", agoStr))
        else
            self.hudElements.bumpVal:SetText("|c888888Never|r")
        end
    end

    -- 4. Rosters & Bank Dues
    local rosters = NonContiguousCount(self.savedVars.staff and self.savedVars.staff.rosterSnapshots or {})
    local deposits = self:GetCount("deposits")
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
    bumper:SetDimensions(320, 292)
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

    -- 10. Mid Divider Line
    local divider2 = wm:CreateControl("$(parent)_Div2", bumper, CT_TEXTURE)
    divider2:SetAnchor(TOPLEFT, bumper, TOPLEFT, 8, 170)
    divider2:SetAnchor(TOPRIGHT, bumper, TOPRIGHT, -8, 170)
    divider2:SetHeight(1)
    divider2:SetColor(0.3, 0.3, 0.35, 0.4)

    -- 11. Status / Progress Label
    local statusLbl = wm:CreateControl("$(parent)_Status", bumper, CT_LABEL)
    statusLbl:SetAnchor(TOPLEFT, bumper, TOPLEFT, 12, 174)
    statusLbl:SetAnchor(TOPRIGHT, bumper, TOPRIGHT, -12, 174)
    statusLbl:SetHeight(18)
    statusLbl:SetFont("ZoFontGameSmall")
    statusLbl:SetText("Ready to bump.")
    self.bumperStatusText = statusLbl

    -- 12. Automation Option Divider
    local divider3 = wm:CreateControl("$(parent)_Div3", bumper, CT_TEXTURE)
    divider3:SetAnchor(TOPLEFT, bumper, TOPLEFT, 8, 195)
    divider3:SetAnchor(TOPRIGHT, bumper, TOPRIGHT, -8, 195)
    divider3:SetHeight(1)
    divider3:SetColor(0.8, 0.5, 0.1, 0.25)

    -- 13. Auto-Reload UI Checkbox Row
    local autoReloadRow = wm:CreateControl("$(parent)_AutoReloadRow", bumper, CT_CONTROL)
    autoReloadRow:SetAnchor(TOPLEFT, bumper, TOPLEFT, 10, 199)
    autoReloadRow:SetDimensions(300, 20)
    autoReloadRow:SetMouseEnabled(true)

    local arCheck = wm:CreateControl("$(parent)_Check", autoReloadRow, CT_BUTTON)
    arCheck:SetAnchor(LEFT, autoReloadRow, LEFT, 2, 0)
    arCheck:SetDimensions(18, 18)
    arCheck:SetFont("ZoFontGameBold")
    arCheck:SetText("[ ]")

    local arLbl = wm:CreateControl("$(parent)_Lbl", autoReloadRow, CT_LABEL)
    arLbl:SetAnchor(LEFT, arCheck, RIGHT, 6, 0)
    arLbl:SetFont("ZoFontGameSmall")
    arLbl:SetText("Auto-Reload UI after bump")

    local function ToggleAutoReload()
        if self.isBumping then return end
        local cur = self.savedVars.settings.bumperAutoReload or false
        self.savedVars.settings.bumperAutoReload = not cur
        self:UpdateBumperUI()
    end

    arCheck:SetHandler("OnClicked", ToggleAutoReload)
    autoReloadRow:SetHandler("OnMouseUp", ToggleAutoReload)
    autoReloadRow:SetHandler("OnMouseEnter", function(c)
        InitializeTooltip(InformationTooltip, c, TOP, 0, -4)
        SetTooltipText(InformationTooltip, "Automatically reload the interface once all selected guild stores are bumped, uploading fresh listings to TamrielTradeCentre.")
    end)
    autoReloadRow:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)

    self.bumperAutoReloadCheck = arCheck
    self.bumperAutoReloadLbl = arLbl

    -- 14. Wait for LibHistoire Checkbox Row (Subordinate)
    local waitLHRow = wm:CreateControl("$(parent)_WaitLHRow", bumper, CT_CONTROL)
    waitLHRow:SetAnchor(TOPLEFT, bumper, TOPLEFT, 20, 221)
    waitLHRow:SetDimensions(290, 20)
    waitLHRow:SetMouseEnabled(true)

    local wlhCheck = wm:CreateControl("$(parent)_Check", waitLHRow, CT_BUTTON)
    wlhCheck:SetAnchor(LEFT, waitLHRow, LEFT, 2, 0)
    wlhCheck:SetDimensions(18, 18)
    wlhCheck:SetFont("ZoFontGameBold")
    wlhCheck:SetText("[ ]")

    local wlhLbl = wm:CreateControl("$(parent)_Lbl", waitLHRow, CT_LABEL)
    wlhLbl:SetAnchor(LEFT, wlhCheck, RIGHT, 6, 0)
    wlhLbl:SetFont("ZoFontGameSmall")
    wlhLbl:SetText("Wait for LibHistoire requests")

    local function ToggleWaitLH()
        if self.isBumping then return end
        if not (self.savedVars.settings.bumperAutoReload) then return end
        local cur = self.savedVars.settings.bumperWaitForLibHistoire or false
        self.savedVars.settings.bumperWaitForLibHistoire = not cur
        self:UpdateBumperUI()
    end

    wlhCheck:SetHandler("OnClicked", ToggleWaitLH)
    waitLHRow:SetHandler("OnMouseUp", ToggleWaitLH)
    waitLHRow:SetHandler("OnMouseEnter", function(c)
        InitializeTooltip(InformationTooltip, c, TOP, 0, -4)
        SetTooltipText(InformationTooltip, "Ensure active LibHistoire guild history server requests and event queues finish before reloading UI, preventing interrupted history synchronization.")
    end)
    waitLHRow:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)

    self.bumperWaitLHCheck = wlhCheck
    self.bumperWaitLHLbl = wlhLbl

    -- 15. Action Button Divider
    local divider4 = wm:CreateControl("$(parent)_Div4", bumper, CT_TEXTURE)
    divider4:SetAnchor(TOPLEFT, bumper, TOPLEFT, 8, 245)
    divider4:SetAnchor(TOPRIGHT, bumper, TOPRIGHT, -8, 245)
    divider4:SetHeight(1)
    divider4:SetColor(0.3, 0.3, 0.35, 0.4)

    -- 16. Bump Action Button [⚡ Bump Selected Guilds]
    local bumpBtn = wm:CreateControl("$(parent)_BumpBtn", bumper, CT_BUTTON)
    bumpBtn:SetAnchor(BOTTOMLEFT, bumper, BOTTOMLEFT, 12, -8)
    bumpBtn:SetAnchor(BOTTOMRIGHT, bumper, BOTTOMRIGHT, -12, -8)
    bumpBtn:SetHeight(26)
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

    -- 13. ReloadUI relocated to right-click context on bumper header title (H7 safety fix)
    --     No longer next to the Bump button — prevents accidental UI reloads mid-session.
    local headerClickZone = wm:CreateControl("$(parent)_HeaderClick", bumper, CT_CONTROL)
    headerClickZone:SetAnchor(TOPLEFT, bumper, TOPLEFT, 0, 0)
    headerClickZone:SetDimensions(320, 30)
    headerClickZone:SetMouseEnabled(true)
    headerClickZone:SetHandler("OnMouseUp", function(ctrl, btn)
        if btn == MOUSE_BUTTON_INDEX_RIGHT then
            ClearMenu()
            AddMenuItem("|cFF9900[Debug]|r Reload UI", function()
                ReloadUI()
            end)
            ShowMenu(ctrl)
        end
    end)
    headerClickZone:SetHandler("OnMouseEnter", function(ctrl)
        InitializeTooltip(InformationTooltip, ctrl, BOTTOM, 0, 4)
        SetTooltipText(InformationTooltip, "Right-click for debug options.")
    end)
    headerClickZone:SetHandler("OnMouseExit", function()
        ClearTooltip(InformationTooltip)
    end)
    self.bumperReloadBtn = nil  -- no longer a standalone button (H7)

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
                -- H5: filled amber square for selected guild
                rowData.checkBtn:SetText("|cFFB347■|r")
            else
                -- H5: hollow square for unselected guild
                rowData.checkBtn:SetText("|c555555□|r")
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
        end
    end

    -- Update Auto-Reload UI and Wait for LibHistoire toggles
    local isAutoReload = self.savedVars and self.savedVars.settings and self.savedVars.settings.bumperAutoReload
    local isWaitLH = self.savedVars and self.savedVars.settings and self.savedVars.settings.bumperWaitForLibHistoire

    if self.bumperAutoReloadCheck then
        if isAutoReload then
            self.bumperAutoReloadCheck:SetText("|c59E08A■|r")
            if self.bumperAutoReloadLbl then
                self.bumperAutoReloadLbl:SetText("|cFFFFFFAuto-Reload UI after bump|r")
            end
        else
            self.bumperAutoReloadCheck:SetText("|c555555□|r")
            if self.bumperAutoReloadLbl then
                self.bumperAutoReloadLbl:SetText("|c888888Auto-Reload UI after bump|r")
            end
        end
    end

    if self.bumperWaitLHCheck then
        if not isAutoReload then
            self.bumperWaitLHCheck:SetText("|c333333□|r")
            if self.bumperWaitLHLbl then
                self.bumperWaitLHLbl:SetText("|c555555Wait for LibHistoire requests|r")
            end
        elseif isWaitLH then
            self.bumperWaitLHCheck:SetText("|c00FFCC■|r")
            if self.bumperWaitLHLbl then
                self.bumperWaitLHLbl:SetText("|c00FFCCWait for LibHistoire requests|r")
            end
        else
            self.bumperWaitLHCheck:SetText("|c555555□|r")
            if self.bumperWaitLHLbl then
                self.bumperWaitLHLbl:SetText("|c888888Wait for LibHistoire requests|r")
            end
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
     MOTD RAFFLE MANAGER UI (Interactive Editor & Live Character Gauge)
========================================================================= ]]--

local MAX_MOTD_CHARS = MAX_GUILD_MOTD_LENGTH or 2048

local RAFFLE_TEMPLATE_BLOCK = [[|cFFD700★ WEEKLY GUILD RAFFLE ★|r
Pot: currently at |cFFD7000|r |t16:16:EsoUI/Art/currency/currency_gold.dds|t
Pool: |c00FFCCtickets in pool|r |c00FFCC0|r |t16:16:EsoUI/Art/icons/quest_ticket.dds|t
Participants: |cFFFFFFentrants|r |cFFFFFF0|r |t16:16:EsoUI/Art/compass/compass_groupLeader.dds|t
Total Deposits: |cFFFFFFentries|r |cFFFFFF0|r |t16:16:EsoUI/Art/icons/icon_experience.dds|t]]

function FR:UpdateMotDGauge()
    if not self.motdEditBox or not self.motdGaugeLbl then return end
    local text = self.motdEditBox:GetText() or ""
    local charCount = (zo_strlen and zo_strlen(text)) or #text
    local byteCount = #text

    local colorCode = "00FF00"
    local statusNote = string.format("%d characters remaining", MAX_MOTD_CHARS - charCount)
    if charCount > MAX_MOTD_CHARS then
        colorCode = "FF5555"
        statusNote = string.format("|cFF5555+%d characters OVER the %d limit!|r", charCount - MAX_MOTD_CHARS, MAX_MOTD_CHARS)
    elseif charCount > (MAX_MOTD_CHARS - 100) then
        colorCode = "FFCC00"
        statusNote = string.format("|cFFCC00%d characters remaining (Near limit)|r", MAX_MOTD_CHARS - charCount)
    end

    self.motdGaugeLbl:SetText(string.format("Length: |c%s%d / %d characters|r (|c888888%s bytes|r) • %s",
        colorCode, charCount, MAX_MOTD_CHARS, ZO_LocalizeDecimalNumber(byteCount), statusNote))
end

function FR:CreateMotDUI()
    if self.motdWindow then return end
    if not self.savedVars or not self.savedVars.settings then return end

    local wm = WINDOW_MANAGER

    -- 1. Main TopLevelWindow (Draggable, movable, clamped)
    local motdWin = wm:CreateTopLevelWindow("FissalRelay_MotDUI")
    motdWin:SetDimensions(640, 560)
    motdWin:SetClampedToScreen(true)
    motdWin:SetMouseEnabled(true)
    motdWin:SetMovable(true)
    motdWin:SetHidden(true)

    -- Position restoration
    local pos = self.savedVars.settings.motdPos
    motdWin:ClearAnchors()
    if pos and pos.x and pos.y and (pos.x ~= 0 or pos.y ~= 0) then
        motdWin:SetAnchor(TOPLEFT, GuiRoot, TOPLEFT, pos.x, pos.y)
    else
        motdWin:SetAnchor(CENTER, GuiRoot, CENTER, 0, -20)
    end

    motdWin:SetHandler("OnMoveStop", function(control)
        self.savedVars.settings.motdPos = {
            x = control:GetLeft(),
            y = control:GetTop(),
        }
    end)

    if UISpecialWindows then
        table.insert(UISpecialWindows, "FissalRelay_MotDUI")
    end

    -- 2. Dark Tinted Backdrop
    local backdrop = wm:CreateControl("$(parent)_Backdrop", motdWin, CT_BACKDROP)
    backdrop:SetAnchorFill()
    backdrop:SetCenterColor(0.04, 0.04, 0.06, 0.94)
    backdrop:SetEdgeColor(0.75, 0.50, 0.10, 0.95)
    backdrop:SetEdgeTexture("", 8, 1, 0)

    -- 3. Munge Bezel Texture
    local munge = wm:CreateControl("$(parent)_Munge", motdWin, CT_TEXTURE)
    munge:SetAnchorFill()
    munge:SetTexture("EsoUI/Art/Performance/StatusMeterMunge.dds")
    munge:SetAlpha(0.65)

    -- 4. Header Icon
    local icon = wm:CreateControl("$(parent)_Icon", motdWin, CT_TEXTURE)
    icon:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 12, 10)
    icon:SetDimensions(22, 22)
    icon:SetTexture("EsoUI/Art/MainMenu/menuBar_guilds_up.dds")

    -- 5. Title
    local title = wm:CreateControl("$(parent)_Title", motdWin, CT_LABEL)
    title:SetAnchor(LEFT, icon, RIGHT, 8, 0)
    title:SetFont("ZoFontGameBold")
    title:SetText("|cFF9900FISSAL|r |c00FFCCMOTD RAFFLE MANAGER|r")

    -- 6. Close Button [×]
    local closeBtn = wm:CreateControl("$(parent)_Close", motdWin, CT_BUTTON)
    closeBtn:SetAnchor(TOPRIGHT, motdWin, TOPRIGHT, -10, 8)
    closeBtn:SetDimensions(20, 20)
    closeBtn:SetFont("ZoFontGameBold")
    closeBtn:SetNormalFontColor(0.7, 0.7, 0.7, 1)
    closeBtn:SetMouseOverFontColor(1, 0.3, 0.3, 1)
    closeBtn:SetText("×")
    closeBtn:SetHandler("OnClicked", function()
        self:ToggleMotDUI(false)
    end)

    -- 7. Header Divider
    local divider = wm:CreateControl("$(parent)_Div1", motdWin, CT_TEXTURE)
    divider:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 10, 34)
    divider:SetAnchor(TOPRIGHT, motdWin, TOPRIGHT, -10, 34)
    divider:SetHeight(1)
    divider:SetColor(0.8, 0.5, 0.1, 0.4)

    -- 8. Guild Selector Tabs
    self.motdGuildButtons = {}
    self.motdSelectedGuildIndex = 1
    self.motdLookbackDays = 7

    for i = 1, 5 do
        local btn = wm:CreateControl("$(parent)_GuildTab_" .. i, motdWin, CT_BUTTON)
        btn:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 12 + (i - 1) * 123, 38)
        btn:SetDimensions(120, 24)
        btn:SetFont("ZoFontGameSmall")
        btn:SetNormalFontColor(0.7, 0.7, 0.7, 1)
        btn:SetMouseOverFontColor(1, 0.9, 0.4, 1)
        btn:SetText(string.format("Guild %d", i))
        btn:SetHandler("OnClicked", function()
            self.motdSelectedGuildIndex = i
            self:UpdateMotDUI(true)
        end)
        self.motdGuildButtons[i] = btn
    end

    -- 9. Metrics & Lookback Inset Card
    local metricsCard = wm:CreateControl("$(parent)_MetricsCard", motdWin, CT_BACKDROP)
    metricsCard:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 12, 66)
    metricsCard:SetAnchor(TOPRIGHT, motdWin, TOPRIGHT, -12, 66)
    metricsCard:SetHeight(58)
    metricsCard:SetCenterColor(0.06, 0.06, 0.08, 0.85)
    metricsCard:SetEdgeColor(0.30, 0.25, 0.18, 0.70)
    metricsCard:SetEdgeTexture("", 8, 1, 0)

    local lookbackLbl = wm:CreateControl("$(parent)_LookbackLbl", metricsCard, CT_LABEL)
    lookbackLbl:SetAnchor(TOPLEFT, metricsCard, TOPLEFT, 8, 8)
    lookbackLbl:SetFont("ZoFontGameSmall")
    lookbackLbl:SetColor(0.8, 0.8, 0.8, 1)
    lookbackLbl:SetText("Lookback:")

    self.motdLookbackBtns = {}
    local dayOptions = { 7, 14, 30 }
    for idx, d in ipairs(dayOptions) do
        local dBtn = wm:CreateControl("$(parent)_DayBtn_" .. d, metricsCard, CT_BUTTON)
        dBtn:SetAnchor(TOPLEFT, metricsCard, TOPLEFT, 8 + (idx - 1) * 36, 26)
        dBtn:SetDimensions(32, 22)
        dBtn:SetFont("ZoFontGameSmall")
        dBtn:SetText(string.format("%dd", d))
        dBtn:SetHandler("OnClicked", function()
            self.motdLookbackDays = d
            self:UpdateMotDUI(false)
        end)
        self.motdLookbackBtns[d] = dBtn
    end

    local recalcBtn = wm:CreateControl("$(parent)_RecalcBtn", metricsCard, CT_BUTTON)
    recalcBtn:SetAnchor(TOPLEFT, metricsCard, TOPLEFT, 120, 26)
    recalcBtn:SetDimensions(26, 22)
    recalcBtn:SetFont("ZoFontGameSmall")
    recalcBtn:SetNormalFontColor(0, 1, 0.8, 1)
    recalcBtn:SetText("↻")
    recalcBtn:SetHandler("OnClicked", function()
        self:UpdateMotDUI(false)
    end)

    local potLbl = wm:CreateControl("$(parent)_PotLbl", metricsCard, CT_LABEL)
    potLbl:SetAnchor(TOPLEFT, metricsCard, TOPLEFT, 155, 8)
    potLbl:SetFont("ZoFontGame")
    potLbl:SetText("Pot: |cFFD7000|r")
    self.motdPotLbl = potLbl

    local tixLbl = wm:CreateControl("$(parent)_TixLbl", metricsCard, CT_LABEL)
    tixLbl:SetAnchor(TOPLEFT, metricsCard, TOPLEFT, 380, 8)
    tixLbl:SetFont("ZoFontGame")
    tixLbl:SetText("Tickets: |c00FFCC0|r")
    self.motdTixLbl = tixLbl

    local entLbl = wm:CreateControl("$(parent)_EntLbl", metricsCard, CT_LABEL)
    entLbl:SetAnchor(TOPLEFT, metricsCard, TOPLEFT, 155, 32)
    entLbl:SetFont("ZoFontGameSmall")
    entLbl:SetText("Entrants: 0 members")
    self.motdEntLbl = entLbl

    local depLbl = wm:CreateControl("$(parent)_DepLbl", metricsCard, CT_LABEL)
    depLbl:SetAnchor(TOPLEFT, metricsCard, TOPLEFT, 380, 32)
    depLbl:SetFont("ZoFontGameSmall")
    depLbl:SetText("Total Deposits: 0")
    self.motdDepLbl = depLbl

    -- 10. Status / Authority Line
    local authLbl = wm:CreateControl("$(parent)_AuthLbl", motdWin, CT_LABEL)
    authLbl:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 14, 128)
    authLbl:SetFont("ZoFontGameSmall")
    authLbl:SetText("Guild: --")
    self.motdAuthLbl = authLbl

    -- 11. MotD Editor Title
    local editorTitle = wm:CreateControl("$(parent)_EditorTitle", motdWin, CT_LABEL)
    editorTitle:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 14, 148)
    editorTitle:SetFont("ZoFontGameBold")
    editorTitle:SetText("Message of the Day (Live Server View / Draft):")

    -- 12. EditBox Container Backdrop
    local editBg = wm:CreateControlFromVirtual("$(parent)_EditBackdrop", motdWin, "ZO_EditBackdrop")
    editBg:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 12, 168)
    editBg:SetAnchor(TOPRIGHT, motdWin, TOPRIGHT, -12, 168)
    editBg:SetHeight(240)

    -- 13. Multi-line EditBox
    local editbox = wm:CreateControlFromVirtual("$(parent)_Edit", editBg, "ZO_DefaultEditMultiLineForBackdrop")
    editbox:SetAnchor(TOPLEFT, editBg, TOPLEFT, 8, 6)
    editbox:SetAnchor(BOTTOMRIGHT, editBg, BOTTOMRIGHT, -8, -6)
    editbox:SetFont("ZoFontGame")
    editbox:SetMaxInputChars(4000)

    editbox:SetHandler("OnMouseWheel", function(ctrl, delta)
        if ctrl:HasFocus() then
            local cursorPos = ctrl:GetCursorPosition()
            local text = ctrl:GetText()
            local textLen = #text
            local newPos
            if delta > 0 then
                local reverseText = text:reverse()
                local revCursorPos = textLen - cursorPos
                local revPos = reverseText:find("\n", revCursorPos + 1)
                newPos = revPos and (textLen - revPos)
            else
                newPos = text:find("\n", cursorPos + 1)
            end
            if newPos then ctrl:SetCursorPosition(newPos) end
        end
    end)

    editbox:SetHandler("OnTextChanged", function(ctrl)
        self:UpdateMotDGauge()
    end)

    self.motdEditBox = editbox

    -- 14. Character & Byte Gauge Bar
    local gaugeLbl = wm:CreateControl("$(parent)_GaugeLbl", motdWin, CT_LABEL)
    gaugeLbl:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 14, 412)
    gaugeLbl:SetFont("ZoFontGameBold")
    gaugeLbl:SetText(string.format("Length: 0 / %d characters (0 bytes)", MAX_MOTD_CHARS))
    self.motdGaugeLbl = gaugeLbl

    -- 15. Action Buttons - Row 1 (Raffle Tools)
    local applyBtn = wm:CreateControl("$(parent)_ApplyBtn", motdWin, CT_BUTTON)
    applyBtn:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 12, 436)
    applyBtn:SetDimensions(200, 26)
    applyBtn:SetFont("ZoFontGameBold")
    applyBtn:SetNormalFontColor(1, 0.85, 0.2, 1)
    applyBtn:SetText("⚡ Apply Numbers")
    applyBtn:SetHandler("OnClicked", function()
        self:ApplyRaffleNumbersToEditor()
    end)
    self.motdApplyBtn = applyBtn

    local templateBtn = wm:CreateControl("$(parent)_TemplateBtn", motdWin, CT_BUTTON)
    templateBtn:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 218, 436)
    templateBtn:SetDimensions(200, 26)
    templateBtn:SetFont("ZoFontGameBold")
    templateBtn:SetNormalFontColor(0, 1, 0.8, 1)
    templateBtn:SetText("＋ Insert Template")
    templateBtn:SetHandler("OnClicked", function()
        self:InsertRaffleTemplateToEditor()
    end)

    local previewBtn = wm:CreateControl("$(parent)_PreviewBtn", motdWin, CT_BUTTON)
    previewBtn:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 424, 436)
    previewBtn:SetAnchor(TOPRIGHT, motdWin, TOPRIGHT, -12, 436)
    previewBtn:SetHeight(26)
    previewBtn:SetFont("ZoFontGameBold")
    previewBtn:SetNormalFontColor(0.8, 0.8, 1, 1)
    previewBtn:SetText("💬 Chat Preview")
    previewBtn:SetHandler("OnClicked", function()
        self:UpdateGuildMotDRaffle(self.motdSelectedGuildIndex, true, self.motdLookbackDays or 7)
    end)

    -- 16. Action Buttons - Row 2 (Server Sync)
    local revertBtn = wm:CreateControl("$(parent)_RevertBtn", motdWin, CT_BUTTON)
    revertBtn:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 12, 468)
    revertBtn:SetDimensions(200, 28)
    revertBtn:SetFont("ZoFontGameBold")
    revertBtn:SetNormalFontColor(0.7, 0.7, 0.7, 1)
    revertBtn:SetText("↺ Revert from Server")
    revertBtn:SetHandler("OnClicked", function()
        self:UpdateMotDUI(true)
        if self.motdStatusText then
            self.motdStatusText:SetText("|c00FFCCReverted editor to live server MotD.|r")
        end
    end)

    local pushBtn = wm:CreateControl("$(parent)_PushBtn", motdWin, CT_BUTTON)
    pushBtn:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 218, 468)
    pushBtn:SetAnchor(TOPRIGHT, motdWin, TOPRIGHT, -12, 468)
    pushBtn:SetHeight(28)
    pushBtn:SetFont("ZoFontGameBold")
    pushBtn:SetNormalFontColor(0, 1, 0, 1)
    pushBtn:SetText("✓ Push to Guild Live")
    pushBtn:SetHandler("OnClicked", function()
        self:PushEditorMotDToGuild()
    end)
    self.motdPushBtn = pushBtn

    -- 17. Status / Tip Label
    local statusText = wm:CreateControl("$(parent)_StatusText", motdWin, CT_LABEL)
    statusText:SetAnchor(TOPLEFT, motdWin, TOPLEFT, 14, 502)
    statusText:SetAnchor(TOPRIGHT, motdWin, TOPRIGHT, -14, 502)
    statusText:SetFont("ZoFontGameSmall")
    statusText:SetText("Tip: Click [⚡ Apply Numbers] to update values in-place, then [✓ Push to Guild Live]. Press Esc to close.")
    self.motdStatusText = statusText

    self.motdWindow = motdWin
    self:UpdateMotDUI(true)
end

function FR:UpdateMotDUI(reloadFromGuild)
    if not self.motdWindow then return end

    local numGuilds = GetNumGuilds()
    local selectedIdx = math.max(1, math.min(self.motdSelectedGuildIndex or 1, numGuilds))
    self.motdSelectedGuildIndex = selectedIdx

    -- Update Guild Buttons
    for i = 1, 5 do
        local btn = self.motdGuildButtons[i]
        if btn then
            if i <= numGuilds then
                local gId = GetGuildId(i)
                local gName = GetGuildName(gId)
                btn:SetHidden(false)
                if i == selectedIdx then
                    btn:SetText(string.format("|cFF9900[%d] %s|r", i, gName:sub(1, 14)))
                else
                    btn:SetText(string.format("[%d] %s", i, gName:sub(1, 14)))
                end
            else
                btn:SetHidden(true)
            end
        end
    end

    -- Update Lookback Buttons
    local curDays = self.motdLookbackDays or 7
    for d, dBtn in pairs(self.motdLookbackBtns or {}) do
        if d == curDays then
            dBtn:SetNormalFontColor(1, 0.85, 0.2, 1)
        else
            dBtn:SetNormalFontColor(0.6, 0.6, 0.6, 1)
        end
    end

    local guildId = GetGuildId(selectedIdx)
    local guildName = GetGuildName(guildId)
    local hasPermission = DoesPlayerHaveGuildPermission and DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_SET_MOTD)
    local isGM = IsPlayerGuildMaster and IsPlayerGuildMaster(guildId)
    local canEdit = isGM or hasPermission

    if self.motdAuthLbl then
        local authStr = canEdit and "|c00FF00Guild Master / Officer (Can Set MotD)|r" or "|cFF5555Read-Only (No Set MotD Permission)|r"
        self.motdAuthLbl:SetText(string.format("Guild: |c00FFCC%s|r (ID: %s) • Authority: %s", guildName, tostring(guildId), authStr))
    end

    -- Calculate Metrics
    local metrics = self:CalculateRaffleMetrics(guildId, curDays, 1000)
    if self.motdPotLbl then
        self.motdPotLbl:SetText(string.format("Pot: |cFFD700%s|r gold", ZO_LocalizeDecimalNumber(metrics.totalGold)))
    end
    if self.motdTixLbl then
        self.motdTixLbl:SetText(string.format("Tickets: |c00FFCC%s|r in pool", ZO_LocalizeDecimalNumber(metrics.totalTickets)))
    end
    if self.motdEntLbl then
        self.motdEntLbl:SetText(string.format("Entrants: |cFFFFFF%d|r members", metrics.entrants))
    end
    if self.motdDepLbl then
        self.motdDepLbl:SetText(string.format("Total Deposits: |cFFFFFF%d|r entries", metrics.entries))
    end

    -- Reload MotD from game server if requested
    if reloadFromGuild and self.motdEditBox then
        local serverMotD = GetGuildMotD(guildId) or ""
        self.motdEditBox:SetText(serverMotD)
    end

    self:UpdateMotDGauge()
end

function FR:ApplyRaffleNumbersToEditor()
    if not self.motdEditBox then return end
    local text = self.motdEditBox:GetText() or ""
    if text == "" then
        if self.motdStatusText then
            self.motdStatusText:SetText("|cFF5555Editor is empty. Click [＋ Insert Template] first.|r")
        end
        return
    end

    local guildId = self:ResolveGuildId(self.motdSelectedGuildIndex or 1)
    local metrics = self:CalculateRaffleMetrics(guildId, self.motdLookbackDays or 7, 1000)
    local goldStr = ZO_LocalizeDecimalNumber(metrics.totalGold)
    local ticketsStr = ZO_LocalizeDecimalNumber(metrics.totalTickets)
    local entrantsStr = tostring(metrics.entrants)
    local entriesStr = tostring(metrics.entries)

    local updated = text
    local ok1, oldGold, ok2, oldTickets, ok3, oldEntrants, ok4, oldEntries

    updated, ok1, oldGold = self:ReplaceRaffleField(updated, "currently at", goldStr)
    updated, ok2, oldTickets = self:ReplaceRaffleField(updated, "tickets in pool", ticketsStr)
    updated, ok3, oldEntrants = self:ReplaceRaffleField(updated, "entrants", entrantsStr)
    updated, ok4, oldEntries = self:ReplaceRaffleField(updated, "entries", entriesStr)

    local matchedAny = ok1 or ok2 or ok3 or ok4
    if not matchedAny then
        if self.motdStatusText then
            self.motdStatusText:SetText("|cFFCC00No raffle fields found in text. Click [＋ Insert Template] below to add the block.|r")
        end
        return
    end

    self.motdEditBox:SetText(updated)
    self:UpdateMotDGauge()
    if self.motdStatusText then
        self.motdStatusText:SetText(string.format("✓ |c00FF00Applied numbers!|r Pot: %s gold (%s tickets, %s entrants).", goldStr, ticketsStr, entrantsStr))
    end
    PlayFissalSound()
end

function FR:InsertRaffleTemplateToEditor()
    if not self.motdEditBox then return end
    local text = self.motdEditBox:GetText() or ""
    local block = RAFFLE_TEMPLATE_BLOCK
    if text == "" then
        self.motdEditBox:SetText(block)
    else
        self.motdEditBox:SetText(text .. "\n\n" .. block)
    end
    self:UpdateMotDGauge()
    if self.motdStatusText then
        self.motdStatusText:SetText("|c00FF00Inserted raffle template block into MotD.|r")
    end
end

function FR:PushEditorMotDToGuild()
    if not self.motdEditBox then return end
    local guildId = self:ResolveGuildId(self.motdSelectedGuildIndex or 1)
    local guildName = GetGuildName(guildId)
    local hasPermission = DoesPlayerHaveGuildPermission and DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_SET_MOTD)
    local isGM = IsPlayerGuildMaster and IsPlayerGuildMaster(guildId)
    if not (isGM or hasPermission) then
        if self.motdStatusText then
            self.motdStatusText:SetText(string.format("|cFF5555Permission Denied:|r You cannot edit MotD for %s.", guildName))
        end
        self.PrintChat(string.format("|cFF5555Permission Denied:|r You do not have permission to edit the Message of the Day for %s.", ColorText(guildName, "00FFCC")))
        return false
    end

    local text = self.motdEditBox:GetText() or ""
    local charCount = (zo_strlen and zo_strlen(text)) or #text
    local byteCount = #text

    if charCount > MAX_MOTD_CHARS then
        if self.motdStatusText then
            self.motdStatusText:SetText(string.format("|cFF5555Cannot Push:|r Message is %d chars (%d chars over %d limit). Please trim before pushing.", charCount, charCount - MAX_MOTD_CHARS, MAX_MOTD_CHARS))
        end
        self.PrintChat(string.format("|cFF5555Push Aborted:|r MotD is %d characters (%d over the %d limit). Please trim text first.", charCount, charCount - MAX_MOTD_CHARS, MAX_MOTD_CHARS))
        return false
    end

    SetGuildMotD(guildId, text)
    if self.motdStatusText then
        self.motdStatusText:SetText(string.format("✓ |c00FF00Successfully pushed MotD to %s!|r (%d/%d characters)", guildName, charCount, MAX_MOTD_CHARS))
    end
    self.PrintChat(string.format("✓ |c00FF00MotD successfully pushed live to %s!|r [|c00FFCC%d/%d chars|r]", ColorText(guildName, "00FFCC"), charCount, MAX_MOTD_CHARS))
    PlayFissalSound()
    return true
end

function FR:ToggleMotDUI(show, initialGuildIndex)
    if initialGuildIndex and tonumber(initialGuildIndex) then
        self.selectedGuildIndex = tonumber(initialGuildIndex)
    end

    -- Redirect to unified Console Tab 2 (MotD Broadcast Studio)
    if self.ToggleConsole then
        self:ToggleConsole(show)
        if show or (show == nil and self.consoleWindow and not self.consoleWindow:IsHidden()) then
            self:SelectConsoleTab(2)
        end
        return
    end

    if not self.motdWindow then
        self:CreateMotDUI()
    end

    if show == nil then
        show = self.motdWindow and self.motdWindow:IsHidden() or false
    end

    if self.motdWindow then
        self.motdWindow:SetHidden(not show)
        if show then
            self:UpdateMotDUI(true)
        end
    end
end

function FR:ResetMotDPosition()
    if not self.motdWindow then
        self:CreateMotDUI()
    end

    if self.motdWindow then
        self.motdWindow:ClearAnchors()
        self.motdWindow:SetAnchor(CENTER, GuiRoot, CENTER, 0, -20)
        self.savedVars.settings.motdPos = { x = 0, y = 0 }
        self.PrintChat("MotD Manager position reset to screen center.")
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
            text = "Fissal watches your guild store transactions, kiosk ground recon, and bank ledgers with clockwork precision, feeding data smoothly to Redfur Relay.",
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
            default = false,
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
            type = "checkbox",
            name = "Automatically Reload UI After Bump",
            tooltip = "Automatically execute /reloadui once all selected guild stores are bumped to write TTC listings to disk.",
            getFunc = function() return FR.savedVars.settings.bumperAutoReload end,
            setFunc = function(value)
                FR.savedVars.settings.bumperAutoReload = value
                if FR.UpdateBumperUI then FR:UpdateBumperUI() end
            end,
            default = false,
        },
        {
            type = "checkbox",
            name = "Wait for LibHistoire Requests",
            tooltip = "When auto-reloading UI after a bump, wait for any active LibHistoire guild history server requests or event queues to settle before reloading.",
            getFunc = function() return FR.savedVars.settings.bumperWaitForLibHistoire end,
            setFunc = function(value)
                FR.savedVars.settings.bumperWaitForLibHistoire = value
                if FR.UpdateBumperUI then FR:UpdateBumperUI() end
            end,
            disabled = function() return not FR.savedVars.settings.bumperAutoReload end,
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
            text = "Visiting any Guild Trader in Tamriel automatically records the holding guild and coordinates, grounding Redfur Relay with authoritative in-game truth.",
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
    }

    -- Staff Management Tools (only visible to officers/GM)
    if FR:IsPlayerOfficerInAnyGuild() then
        table.insert(optionsData, {
            type = "header",
            name = "Staff Management Tools",
        })
        table.insert(optionsData, {
            type = "checkbox",
            name = "Auto Roster Snapshot on Login",
            tooltip = "Automatically snapshot guild rosters 10 seconds after logging in.",
            getFunc = function() return FR.savedVars.settings.autoRosterSnapshotOnLogin end,
            setFunc = function(value) FR.savedVars.settings.autoRosterSnapshotOnLogin = value end,
            default = true,
        })
        table.insert(optionsData, {
            type = "button",
            name = "Audit Inactives (Guild 1, 14d)",
            tooltip = "Scan members inactive > 14 days and print summary for Discord purge.",
            func = function()
                FR:AuditInactives(1, 14)
            end,
            width = "half",
        })
        table.insert(optionsData, {
            type = "button",
            name = "Audit Bank Dues (Guild 1, 7d)",
            tooltip = "Aggregate weekly gold deposits for raffle tickets and dues.",
            func = function()
                FR:AuditBankDues(1, 7)
            end,
            width = "half",
        })
        table.insert(optionsData, {
            type = "button",
            name = "Snapshot Rosters Now",
            tooltip = "Capture current member list, ranks, and notes for all guilds.",
            func = function()
                local count = FR:TakeRosterSnapshot()
                d(string.format("|cFF9900[Fissal]|r Snapped roster for %d guild(s).", count))
            end,
            width = "full",
        })
        table.insert(optionsData, {
            type = "button",
            name = "Open MotD Raffle Manager",
            tooltip = "Open the visual MotD editor with live character limit counter, raffle metrics, and template insertion.",
            func = function()
                if FR.ToggleMotDUI then
                    FR:ToggleMotDUI(true)
                end
            end,
            width = "full",
        })
        table.insert(optionsData, {
            type = "button",
            name = "Preview MotD Raffle (Guild 1)",
            tooltip = "Preview updated raffle pot, tickets, entrants, and entries in chat without saving.",
            func = function()
                if FR.UpdateGuildMotDRaffle then
                    FR:UpdateGuildMotDRaffle(1, true, 7)
                end
            end,
            width = "half",
        })
        table.insert(optionsData, {
            type = "button",
            name = "Push MotD Raffle (Guild 1)",
            tooltip = "Surgically update the in-game Message of the Day with fresh raffle data from bank deposits.",
            func = function()
                if FR.UpdateGuildMotDRaffle then
                    FR:UpdateGuildMotDRaffle(1, false, 7)
                end
            end,
            width = "half",
        })
        table.insert(optionsData, {
            type = "header",
            name = "Raffle Mail Payout Assistant",
        })
        table.insert(optionsData, {
            type = "checkbox",
            name = "Auto-Show on Mail Compose",
            tooltip = "Automatically open the Raffle Payout Assistant whenever you compose a mail in the mailbox.",
            getFunc = function()
                if FR.savedVars and FR.savedVars.settings and FR.savedVars.settings.raffleMail then
                    return FR.savedVars.settings.raffleMail.autoShowOnMail ~= false
                end
                return true
            end,
            setFunc = function(value)
                if not FR.savedVars.settings.raffleMail then FR.savedVars.settings.raffleMail = {} end
                FR.savedVars.settings.raffleMail.autoShowOnMail = value
            end,
            default = true,
        })
        table.insert(optionsData, {
            type = "checkbox",
            name = "Only Auto-Show if Pending Payouts Exist",
            tooltip = "When enabled, the Raffle Assistant only opens automatically if there are unpaid winners in the active ledger. Once all winners are marked [PAID], regular mail opens unobstructed.",
            getFunc = function()
                if FR.savedVars and FR.savedVars.settings and FR.savedVars.settings.raffleMail then
                    return FR.savedVars.settings.raffleMail.onlyShowIfPending ~= false
                end
                return true
            end,
            setFunc = function(value)
                if not FR.savedVars.settings.raffleMail then FR.savedVars.settings.raffleMail = {} end
                FR.savedVars.settings.raffleMail.onlyShowIfPending = value
            end,
            disabled = function()
                return FR.savedVars and FR.savedVars.settings and FR.savedVars.settings.raffleMail and FR.savedVars.settings.raffleMail.autoShowOnMail == false
            end,
            default = true,
        })
        table.insert(optionsData, {
            type = "button",
            name = "Open Raffle Mail Assistant",
            tooltip = "Open the Raffle Mail Assistant docking window to view winners and auto-fill payout mails.",
            func = function()
                if FR.ToggleRaffleMailUI then
                    FR:ToggleRaffleMailUI(true)
                end
            end,
            width = "full",
        })
    end -- Staff tools rank gate

    table.insert(optionsData, {
        type = "header",
        name = "Guild Trader Sales Tracking",
    })
    table.insert(optionsData, {
        type = "description",
        text = "Fissal monitors your guild store sales using LibHistoire. Guilds that do not have a Trading House unlocked (less than 50 members) are automatically excluded to prevent request lockouts.",
    })

    local numGuilds = GetNumGuilds()
    for i = 1, numGuilds do
        local guildId = GetGuildId(i)
        local guildName = GetGuildName(guildId)
        local hasTrader = DoesGuildHavePrivilege and DoesGuildHavePrivilege(guildId, GUILD_PRIVILEGE_TRADING_HOUSE)

        local traderStatus
        if hasTrader then
            traderStatus = "|c00FF00(Store Active)|r"
        else
            traderStatus = "|c888888(No Trading House - Skipped)|r"
        end

        table.insert(optionsData, {
            type = "checkbox",
            name = string.format("%s %s", guildName, traderStatus),
            tooltip = string.format("Enable or disable trader sales tracking for %s.", guildName),
            getFunc = function()
                if FR.savedVars and FR.savedVars.settings and FR.savedVars.settings.traderGuilds then
                    local val = FR.savedVars.settings.traderGuilds[guildId]
                    if val ~= nil then return val end
                end
                return hasTrader and true or false
            end,
            setFunc = function(value)
                if not FR.savedVars.settings.traderGuilds then
                    FR.savedVars.settings.traderGuilds = {}
                end
                FR.savedVars.settings.traderGuilds[guildId] = value
                if FR.SetupProcessors then FR:SetupProcessors() end
                if FR.UpdateHUD then FR:UpdateHUD() end
            end,
            default = hasTrader and true or false,
        })
    end

    table.insert(optionsData, {
        type = "header",
        name = "Guild Bank Deposit Tracking",
    })
    table.insert(optionsData, {
        type = "description",
        text = "Fissal verifies rank permissions before requesting guild bank deposits and bids. Guilds where you do not have permission to view bank gold are automatically skipped to prevent request stalls and queue lockouts.",
    })

    numGuilds = GetNumGuilds()
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
    if FR.CreateConsoleUI then
        FR:CreateConsoleUI()
    else
        FR:CreateMotDUI()
    end
end

EVENT_MANAGER:RegisterForEvent("FissalRelay_UI", EVENT_PLAYER_ACTIVATED, OnPlayerActivated)
