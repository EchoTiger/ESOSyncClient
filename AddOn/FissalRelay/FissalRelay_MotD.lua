--[[
    FissalRelay_MotD.lua
    Message of the Day (MotD) Broadcast Studio & Token Engine for Fissal Relay Prime
    Crafted by Echo & Fissal for Fissal Relay and the Redfur Guilds.

    Features:
      • Multi-guild MotD drafting board with live character & byte counter (1024 limit)
      • Dynamic Token Engine:
          {raffle_pot}, {raffle_tickets}, {raffle_entrants}, {raffle_first},
          {raffle_second}, {raffle_third}, {raffle_winners}, {kiosk_location},
          {guild_name}, {date_week}
      • Quick-insert token chips for rapid composition
      • Preset template vault (Raffle Push, Winners Announcement, Trader Update, Custom)
      • In-game Chat Preview & 1-click live guild broadcast with permission guard
]]--

FissalRelay = FissalRelay or {}
local FR = FissalRelay

local MAX_MOTD_CHARS = 1024

-- Default operational presets
local DEFAULT_PRESETS = {
    raffle_push = {
        name = "Weekly Raffle Push",
        text = "|cFFD700* {guild_name} WEEKLY RAFFLE *|r\nPot: |cFFD700{raffle_pot}|r gold | Tickets: |c00FFCC{raffle_tickets}|r | Entrants: |cFFFFFF{raffle_entrants}|r\n1st: |cFFD700{raffle_first}|r | 2nd: |cFFAA00{raffle_second}|r | 3rd: |cFF8800{raffle_third}|r\nDeposit 1,000g in guild bank for 1 ticket! Drawing Sunday.\nKiosk: |c59E08A{kiosk_location}|r",
    },
    winners = {
        name = "Winners Announcement",
        text = "|cFFD700* {guild_name} RAFFLE WINNERS ({date_week}) *|r\nTotal Pot: |cFFD700{raffle_pot}|r gold!\n{raffle_winners}\nCongratulations! Gold has been dispatched by staff courier.\nNext week's raffle is now LIVE. Good luck!",
    },
    trader_update = {
        name = "Trader Kiosk Notice",
        text = "|c00FFCC* {guild_name} TRADER UPDATE *|r\nCurrent Kiosk: |c59E08A{kiosk_location}|r\nAll store sales and bank deposits directly fund our weekly trader bid!\nKeep listings stocked with 30 items. Thank you for your support!",
    }
}

--[[ =========================================================================
     TOKEN RESOLUTION ENGINE
========================================================================= ]]--

function FR:ResolveMotDTokens(rawText, guildId)
    if not rawText or rawText == "" then return "" end
    guildId = guildId or GetGuildId(self.selectedGuildIndex or 1)
    local guildName = GetGuildName(guildId)

    local isPost = string.find(guildName, "Post") ~= nil
    local isDealers = string.find(guildName, "Dealer") ~= nil
    local gKey = isPost and "post" or (isDealers and "dealers" or nil)
    local raffleData = gKey and self.GetRaffleData and self:GetRaffleData(gKey)

    -- Kiosk lookup
    local kioskStr = "No Active Kiosk"
    if self.savedVars and self.savedVars.kiosks then
        for trader, data in pairs(self.savedVars.kiosks) do
            if data.guildName == guildName then
                kioskStr = string.format("%s (%s, %s)", trader, data.city or "Tamriel", data.zone or "Tamriel")
                break
            end
        end
    end

    local function FormatGoldVal(n)
        n = tonumber(n) or 0
        if ZO_LocalizeDecimalNumber then return ZO_LocalizeDecimalNumber(n) end
        return tostring(n)
    end

    local potStr = raffleData and FormatGoldVal(raffleData.pot) or "0"
    local tixStr = raffleData and FormatGoldVal(raffleData.tickets) or "0"
    local entStr = raffleData and tostring(raffleData.entrants or 0) or "0"
    local dateStr = raffleData and raffleData.weekLabel or "Current Week"

    local firstStr = raffleData and raffleData.prizes and FormatGoldVal(raffleData.prizes.first) or "0"
    local secondStr = raffleData and raffleData.prizes and FormatGoldVal(raffleData.prizes.second) or "0"
    local thirdStr = raffleData and raffleData.prizes and FormatGoldVal(raffleData.prizes.third) or "0"

    local winnersStr = ""
    if raffleData and raffleData.winners and #raffleData.winners > 0 then
        local parts = {}
        for _, w in ipairs(raffleData.winners) do
            local placeNum = tonumber(w.place) or 0
            local winnerName = tostring(w.name or "Winner")
            table.insert(parts, string.format("#%d %s (%s gold)", placeNum, winnerName, FormatGoldVal(w.prize)))
        end
        winnersStr = table.concat(parts, " | ")
    else
        winnersStr = "No winners recorded."
    end

    local tokens = {
        ["{raffle_pot}"] = potStr,
        ["{raffle_tickets}"] = tixStr,
        ["{raffle_entrants}"] = entStr,
        ["{raffle_first}"] = firstStr,
        ["{raffle_second}"] = secondStr,
        ["{raffle_third}"] = thirdStr,
        ["{raffle_winners}"] = winnersStr,
        ["{kiosk_location}"] = kioskStr,
        ["{guild_name}"] = guildName,
        ["{date_week}"] = dateStr,
    }

    local resolved = rawText
    for token, val in pairs(tokens) do
        -- Escape magic patterns in search string
        local pat = string.gsub(token, "([%(%)%.%%%+%-%*%?%[%]%^%$])", "%%%1")
        -- Pass function replacer so % in replacement value is never treated as pattern capture
        resolved = string.gsub(resolved, pat, function() return val end)
    end

    return resolved
end

--[[ =========================================================================
     MOTD STUDIO UI CONSTRUCTION
========================================================================= ]]--

function FR:BuildMotDStudio(parent)
    local wm = WINDOW_MANAGER
    local panel = wm:CreateControl("FissalRelay_Console_Tab2", parent, CT_CONTROL)
    panel:SetAnchorFill()
    panel:SetHidden(true)
    self.consoleTabs[2] = panel

    -- 1. Main Editor Container Card
    local card = wm:CreateControl("$(parent)_Card", panel, CT_BACKDROP)
    card:SetAnchorFill()
    card:SetCenterColor(0.06, 0.06, 0.08, 0.85)
    card:SetEdgeColor(0.30, 0.25, 0.18, 0.70)
    card:SetEdgeTexture("", 8, 1, 0)

    -- 2. Title & Live Status
    local title = wm:CreateControl("$(parent)_Title", card, CT_LABEL)
    title:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 10)
    title:SetFont("ZoFontGameBold")
    title:SetText("|cFF9900MOTD BROADCAST STUDIO|r • |c00FFCCDynamic Token Interpolation|r")

    local authLbl = wm:CreateControl("$(parent)_AuthLbl", card, CT_LABEL)
    authLbl:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 10)
    authLbl:SetFont("ZoFontGameSmall")
    authLbl:SetText("Permission: Checking...")
    self.motdAuthLbl = authLbl

    -- 3. Token Quick-Insert Chips Bar
    local chipBarLbl = wm:CreateControl("$(parent)_ChipLbl", card, CT_LABEL)
    chipBarLbl:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 34)
    chipBarLbl:SetFont("ZoFontGameSmall")
    chipBarLbl:SetText("|c888888Insert Token:|r")

    local chips = {
        { label = "+ Pot", token = "{raffle_pot}" },
        { label = "+ Tickets", token = "{raffle_tickets}" },
        { label = "+ Entrants", token = "{raffle_entrants}" },
        { label = "+ Prizes", token = "1st: {raffle_first} | 2nd: {raffle_second}" },
        { label = "+ Winners", token = "{raffle_winners}" },
        { label = "+ Kiosk", token = "{kiosk_location}" },
    }

    local xOffset = 90
    for idx, c in ipairs(chips) do
        local cBtn = wm:CreateControl("$(parent)_Chip_" .. idx, card, CT_BUTTON)
        cBtn:SetAnchor(TOPLEFT, card, TOPLEFT, xOffset, 31)
        cBtn:SetDimensions(85, 20)
        cBtn:SetFont("ZoFontGameSmall")
        cBtn:SetText(c.label)
        self:StyleTactileButton(cBtn, {
            normalBg = { 0.04, 0.08, 0.08, 0.85 },
            hoverBg = { 0.06, 0.16, 0.16, 0.95 },
            normalEdge = { 0, 0.60, 0.50, 0.65 },
            hoverEdge = { 0, 1.00, 0.85, 1.00 },
            normalTextColor = { 0, 0.95, 0.8, 1 },
            hoverTextColor = { 0.5, 1, 0.9, 1 },
            tooltipTitle = "Insert Token: " .. c.token,
            tooltipText = string.format("Insert the live %s token into the editor at cursor position.", c.token),
        })
        cBtn:SetHandler("OnClicked", function()
            self:InsertTokenIntoMotDEditBox(c.token)
        end)
        xOffset = xOffset + 90
    end

    -- 4. Multi-line EditBox Container
    local editBg = wm:CreateControlFromVirtual("$(parent)_EditBackdrop", card, "ZO_EditBackdrop")
    editBg:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 56)
    editBg:SetAnchor(BOTTOMRIGHT, card, BOTTOMRIGHT, -12, -96)

    local editbox = wm:CreateControlFromVirtual("$(parent)_Edit", editBg, "ZO_DefaultEditMultiLineForBackdrop")
    editbox:SetAnchor(TOPLEFT, editBg, TOPLEFT, 8, 6)
    editbox:SetAnchor(BOTTOMRIGHT, editBg, BOTTOMRIGHT, -8, -6)
    editbox:SetFont("ZoFontGame")
    editbox:SetMaxInputChars(4000)

    editbox:SetHandler("OnTextChanged", function(ctrl)
        self:UpdateMotDStudioGauge()
    end)

    self.motdStudioEditBox = editbox

    -- 5. Character & Byte Gauge Bar (Dedicated Full-Width Status Line)
    local gaugeLbl = wm:CreateControl("$(parent)_GaugeLbl", card, CT_LABEL)
    gaugeLbl:SetAnchor(TOPLEFT, editBg, BOTTOMLEFT, 4, 6)
    gaugeLbl:SetAnchor(TOPRIGHT, editBg, BOTTOMRIGHT, -4, 6)
    gaugeLbl:SetFont("ZoFontGameBold")
    gaugeLbl:SetText("Length: 0 / 1024 characters (0 bytes)")
    self.motdStudioGaugeLbl = gaugeLbl

    -- 6. Preset Selector Buttons (Dedicated Mid Tier)
    local presetLbl = wm:CreateControl("$(parent)_PresetLbl", card, CT_LABEL)
    presetLbl:SetAnchor(TOPLEFT, editBg, BOTTOMLEFT, 4, 30)
    presetLbl:SetFont("ZoFontGameSmall")
    presetLbl:SetText("|c888888Load Template:|r")

    local pBtn1 = wm:CreateControl("$(parent)_PresetRaffle", card, CT_BUTTON)
    pBtn1:SetAnchor(LEFT, presetLbl, RIGHT, 8, 0)
    pBtn1:SetDimensions(95, 22)
    pBtn1:SetFont("ZoFontGameSmall")
    pBtn1:SetText("Raffle Push")
    self:StyleTactileButton(pBtn1, {
        normalBg = { 0.12, 0.10, 0.04, 0.90 },
        hoverBg = { 0.18, 0.14, 0.06, 0.98 },
        normalEdge = { 0.75, 0.55, 0.10, 0.80 },
        hoverEdge = { 1.00, 0.85, 0.20, 1.00 },
        normalTextColor = { 1, 0.85, 0.2, 1 },
        hoverTextColor = { 1, 0.95, 0.5, 1 },
        tooltipTitle = "Template: Raffle Push",
        tooltipText = "Load weekly raffle announcement template with dynamic ticket prices, pot total, and cutoff date.",
    })
    pBtn1:SetHandler("OnClicked", function()
        self:LoadMotDPreset("raffle_push")
    end)

    local pBtn2 = wm:CreateControl("$(parent)_PresetWinners", card, CT_BUTTON)
    pBtn2:SetAnchor(LEFT, pBtn1, RIGHT, 8, 0)
    pBtn2:SetDimensions(95, 22)
    pBtn2:SetFont("ZoFontGameSmall")
    pBtn2:SetText("Winners")
    self:StyleTactileButton(pBtn2, {
        normalBg = { 0.04, 0.12, 0.12, 0.90 },
        hoverBg = { 0.06, 0.18, 0.18, 0.98 },
        normalEdge = { 0, 0.75, 0.65, 0.80 },
        hoverEdge = { 0, 1.0, 0.85, 1.0 },
        normalTextColor = { 0, 1, 0.8, 1 },
        hoverTextColor = { 0.4, 1, 0.9, 1 },
        tooltipTitle = "Template: Weekly Winners",
        tooltipText = "Load weekly winners congratulation template with 1st, 2nd, 3rd place prizes and discord link.",
    })
    pBtn2:SetHandler("OnClicked", function()
        self:LoadMotDPreset("winners")
    end)

    local pBtn3 = wm:CreateControl("$(parent)_PresetTrader", card, CT_BUTTON)
    pBtn3:SetAnchor(LEFT, pBtn2, RIGHT, 8, 0)
    pBtn3:SetDimensions(95, 22)
    pBtn3:SetFont("ZoFontGameSmall")
    pBtn3:SetText("Trader")
    self:StyleTactileButton(pBtn3, {
        normalBg = { 0.08, 0.08, 0.12, 0.90 },
        hoverBg = { 0.12, 0.12, 0.18, 0.98 },
        normalEdge = { 0.40, 0.40, 0.70, 0.80 },
        hoverEdge = { 0.60, 0.60, 1.00, 1.00 },
        normalTextColor = { 0.8, 0.8, 1, 1 },
        hoverTextColor = { 1, 1, 1, 1 },
        tooltipTitle = "Template: Trader Update",
        tooltipText = "Load trader kiosk update template highlighting our current location and weekly sales target.",
    })
    pBtn3:SetHandler("OnClicked", function()
        self:LoadMotDPreset("trader_update")
    end)

    local clearBtn = wm:CreateControl("$(parent)_ClearBtn", card, CT_BUTTON)
    clearBtn:SetAnchor(TOPRIGHT, editBg, BOTTOMRIGHT, -4, 30)
    clearBtn:SetDimensions(90, 22)
    clearBtn:SetFont("ZoFontGameSmall")
    clearBtn:SetText("Clear Text")
    self:StyleTactileButton(clearBtn, {
        normalBg = { 0.10, 0.05, 0.05, 0.90 },
        hoverBg = { 0.16, 0.08, 0.08, 0.98 },
        normalEdge = { 0.50, 0.25, 0.25, 0.80 },
        hoverEdge = { 0.90, 0.30, 0.30, 1.00 },
        normalTextColor = { 0.9, 0.6, 0.6, 1 },
        hoverTextColor = { 1, 0.8, 0.8, 1 },
        tooltipTitle = "Clear Editor Text",
        tooltipText = "Clear all contents currently inside the MotD editor box.",
    })
    clearBtn:SetHandler("OnClicked", function()
        if self.motdStudioEditBox then
            self.motdStudioEditBox:SetText("")
            self:UpdateMotDStudioGauge()
        end
    end)

    -- 7. Action Bar (Bottom Row)
    local interpolateBtn = wm:CreateControl("$(parent)_InterpolateBtn", card, CT_BUTTON)
    interpolateBtn:SetAnchor(BOTTOMLEFT, card, BOTTOMLEFT, 12, -10)
    interpolateBtn:SetDimensions(150, 28)
    interpolateBtn:SetFont("ZoFontGameBold")
    interpolateBtn:SetText("Interpolate Tokens")
    self:StyleTactileButton(interpolateBtn, {
        normalBg = { 0.12, 0.10, 0.04, 0.90 },
        hoverBg = { 0.18, 0.14, 0.06, 0.98 },
        normalEdge = { 0.75, 0.55, 0.10, 0.80 },
        hoverEdge = { 1.00, 0.85, 0.20, 1.00 },
        normalTextColor = { 1, 0.85, 0.2, 1 },
        hoverTextColor = { 1, 0.95, 0.5, 1 },
        tooltipTitle = "Interpolate Live Tokens",
        tooltipText = "Replace all {tokens} in the editor with live data (pot gold, kiosk location, winners) from the guild ledger.",
    })
    interpolateBtn:SetHandler("OnClicked", function()
        self:ApplyTokensToMotDEditor()
    end)

    local previewBtn = wm:CreateControl("$(parent)_PreviewBtn", card, CT_BUTTON)
    previewBtn:SetAnchor(LEFT, interpolateBtn, RIGHT, 10, 0)
    previewBtn:SetDimensions(130, 28)
    previewBtn:SetFont("ZoFontGameBold")
    previewBtn:SetText("Chat Preview")
    self:StyleTactileButton(previewBtn, {
        normalBg = { 0.08, 0.08, 0.12, 0.90 },
        hoverBg = { 0.12, 0.12, 0.18, 0.98 },
        normalEdge = { 0.40, 0.40, 0.70, 0.80 },
        hoverEdge = { 0.60, 0.60, 1.00, 1.00 },
        normalTextColor = { 0.8, 0.8, 1, 1 },
        hoverTextColor = { 1, 1, 1, 1 },
        tooltipTitle = "Chat Preview",
        tooltipText = "Print the resolved MotD into your local chat window to preview formatting and line breaks before publishing.",
    })
    previewBtn:SetHandler("OnClicked", function()
        self:PreviewMotDInChat()
    end)

    local revertBtn = wm:CreateControl("$(parent)_RevertBtn", card, CT_BUTTON)
    revertBtn:SetAnchor(LEFT, previewBtn, RIGHT, 10, 0)
    revertBtn:SetDimensions(130, 28)
    revertBtn:SetFont("ZoFontGameBold")
    revertBtn:SetText("Revert to Server")
    self:StyleTactileButton(revertBtn, {
        normalBg = { 0.08, 0.08, 0.10, 0.90 },
        hoverBg = { 0.14, 0.14, 0.16, 0.98 },
        normalEdge = { 0.40, 0.40, 0.45, 0.70 },
        hoverEdge = { 0.75, 0.75, 0.85, 1.00 },
        normalTextColor = { 0.75, 0.75, 0.75, 1 },
        hoverTextColor = { 1, 1, 1, 1 },
        tooltipTitle = "Revert to Server",
        tooltipText = "Discard editor changes and reload the live MotD currently active on the guild server.",
    })
    revertBtn:SetHandler("OnClicked", function()
        self:UpdateMotDUI(true)
    end)

    local pushBtn = wm:CreateControl("$(parent)_PushBtn", card, CT_BUTTON)
    pushBtn:SetAnchor(BOTTOMRIGHT, card, BOTTOMRIGHT, -12, -10)
    pushBtn:SetAnchor(LEFT, revertBtn, RIGHT, 10, 0)
    pushBtn:SetHeight(28)
    pushBtn:SetFont("ZoFontGameBold")
    pushBtn:SetText("Push to Guild Live")
    self:StyleTactileButton(pushBtn, {
        normalBg = { 0.04, 0.14, 0.08, 0.90 },
        hoverBg = { 0.06, 0.20, 0.12, 0.98 },
        normalEdge = { 0.20, 0.85, 0.40, 0.85 },
        hoverEdge = { 0.30, 1.00, 0.55, 1.00 },
        normalTextColor = { 0.3, 1, 0.5, 1 },
        hoverTextColor = { 0.6, 1, 0.7, 1 },
        tooltipTitle = "Push to Guild Live",
        tooltipText = "Broadcast this MotD to the guild! Updates the Message of the Day on the ESO server for all guild members.",
    })
    pushBtn:SetHandler("OnClicked", function()
        self:BroadcastMotDToGuild()
    end)
    self.motdPushBtn = pushBtn
end

--[[ =========================================================================
     MOTD STUDIO CONTROLS & ACTIONS
========================================================================= ]]--

function FR:UpdateMotDStudioGauge()
    if not self.motdStudioEditBox or not self.motdStudioGaugeLbl then return end
    local text = self.motdStudioEditBox:GetText() or ""
    local charCount = (zo_strlen and zo_strlen(text)) or #text
    local byteCount = #text

    local colorCode = "59E08A"
    local statusNote = string.format("%d characters remaining", MAX_MOTD_CHARS - charCount)

    if charCount > MAX_MOTD_CHARS then
        colorCode = "FF5555"
        statusNote = string.format("|cFF5555+%d OVER limit!|r", charCount - MAX_MOTD_CHARS)
    elseif charCount > 950 then
        colorCode = "FFCC00"
        statusNote = string.format("|cFFCC00%d characters remaining (Near limit)|r", MAX_MOTD_CHARS - charCount)
    end

    self.motdStudioGaugeLbl:SetText(string.format("Length: |c%s%d / %d characters|r (|c888888%s bytes|r) • %s",
        colorCode, charCount, MAX_MOTD_CHARS, ZO_LocalizeDecimalNumber(byteCount), statusNote))
end

function FR:InsertTokenIntoMotDEditBox(token)
    if not self.motdStudioEditBox then return end
    local cur = self.motdStudioEditBox:GetText() or ""
    local pos = self.motdStudioEditBox:GetCursorPosition() or #cur

    local before = string.sub(cur, 1, pos)
    local after = string.sub(cur, pos + 1)
    local newText = before .. token .. after

    self.motdStudioEditBox:SetText(newText)
    self.motdStudioEditBox:SetCursorPosition(pos + #token)
    self:UpdateMotDStudioGauge()
end

function FR:LoadMotDPreset(presetKey)
    local preset = DEFAULT_PRESETS[presetKey]
    if not preset or not self.motdStudioEditBox then return end
    self.motdStudioEditBox:SetText(preset.text)
    self:UpdateMotDStudioGauge()
    self.PrintChat(string.format("Loaded MotD template: '%s'", preset.name))
end

function FR:ApplyTokensToMotDEditor()
    if not self.motdStudioEditBox then return end
    local raw = self.motdStudioEditBox:GetText() or ""
    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)

    local resolved = self:ResolveMotDTokens(raw, guildId)
    self.motdStudioEditBox:SetText(resolved)
    self:UpdateMotDStudioGauge()
    self.PrintChat("Live guild tokens interpolated into editor.")
end

function FR:PreviewMotDInChat()
    if not self.motdStudioEditBox then return end
    local raw = self.motdStudioEditBox:GetText() or ""
    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)
    local guildName = GetGuildName(guildId)

    local resolved = self:ResolveMotDTokens(raw, guildId)
    self.PrintChat(string.format("=== MotD Preview for %s ===", guildName))
    for line in string.gmatch(resolved .. "\n", "(.-)\r?\n") do
        if line ~= "" then
            df("|c00FFCC%s|r", line)
        end
    end
end

function FR:BroadcastMotDToGuild()
    if not self.motdStudioEditBox then return end
    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)
    local guildName = GetGuildName(guildId)

    if not DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_SET_MOTD) then
        self.PrintChat(string.format("|cFF5555Permission Denied:|r You do not have permission to change the MotD in %s.", guildName))
        return
    end

    local raw = self.motdStudioEditBox:GetText() or ""
    local resolved = self:ResolveMotDTokens(raw, guildId)
    local charCount = (zo_strlen and zo_strlen(resolved)) or #resolved
    local byteCount = #resolved

    if charCount > MAX_MOTD_CHARS or byteCount > MAX_MOTD_CHARS then
        self.PrintChat(string.format("|cFF5555Error:|r MotD exceeds the 1024 limit (%d chars, %d bytes). Please shorten before pushing.", charCount, byteCount))
        return
    end

    SetGuildMotD(guildId, resolved)
    self.PrintChat(string.format("|c59E08ASuccess:|r Pushed updated MotD to %s! (%d chars, %d bytes)", guildName, charCount, byteCount))
    PlaySound(SOUNDS.GUILD_ROSTER_ADDED or SOUNDS.NOTE_SAVED)
end

function FR:UpdateMotDUI(forceServerPull)
    if not self.motdStudioEditBox then return end
    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)
    local guildName = GetGuildName(guildId)

    -- Update permission indicator
    local hasPerm = DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_SET_MOTD)
    if self.motdAuthLbl then
        self.motdAuthLbl:SetText(string.format("MotD Authority: %s",
            hasPerm and "|c59E08A[AUTHORIZED]|r" or "|cFF5555[READ-ONLY]|r"))
    end
    if self.motdPushBtn then
        self.motdPushBtn:SetEnabled(hasPerm)
    end

    -- Pull server MotD if requested or empty
    if forceServerPull or self.motdStudioEditBox:GetText() == "" then
        local liveMotD = GetGuildMotD(guildId) or ""
        self.motdStudioEditBox:SetText(liveMotD)
    end

    self:UpdateMotDStudioGauge()
end
