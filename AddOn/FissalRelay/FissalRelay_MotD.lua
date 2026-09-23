--[[
    FissalRelay_MotD.lua
    Message of the Day (MotD) Broadcast Studio & Token Engine for Fissal Relay Prime
    Crafted by Echo & Fissal for Fissal Relay and the Redfur Guilds.

    Features:
      • Multi-guild MotD drafting board with live character & byte counter (2048 limit)
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

local MAX_MOTD_CHARS = MAX_GUILD_MOTD_LENGTH or 1024

-- Clean UTF-8 byte boundary & tag-aware truncation preventing mojibake & leaked color tags (Fable 5.1 S3 & S4)
function FR:TruncateUtf8(str, maxBytes)
    if not str then return "" end
    maxBytes = maxBytes or (MAX_GUILD_MOTD_LENGTH or 1024)
    if #str <= maxBytes then return str end

    -- Reserve 2 bytes in budget in case we need to append |r for an open color tag
    local budget = math.max(1, maxBytes - 2)
    local cut = budget

    -- Fable 5.1 S4: Inspect byte after cut to prevent mid-character splits without dropping valid multi-byte chars
    while cut > 0 do
        local nextB = string.byte(str, cut + 1)
        if nextB and nextB >= 0x80 and nextB < 0xC0 then
            cut = cut - 1
        else
            break
        end
    end
    local result = string.sub(str, 1, cut)

    -- Check if cut occurred inside or right after an unclosed |c tag (e.g. |c, |c1, |c123456)
    local lastPipeC = string.find(result, "|c[^|]*$")
    if lastPipeC and (#result - lastPipeC) < 8 then
        -- Cut occurred inside a |c tag! Back off before the |c
        result = string.sub(result, 1, lastPipeC - 1)
    end

    -- Fable 5.1 S3 & P2: Remove lone trailing pipe (if odd count of trailing pipes) so appending |r does not produce escaped ||r
    local trailing = #string.match(result, "|*$")
    if trailing % 2 == 1 then
        result = string.sub(result, 1, -2)
    end

    -- Auto-balance unclosed |c color tags (stripping || escapes first so ||c is not counted)
    local unescaped = string.gsub(result, "||", "")
    local _, colorStarts = string.gsub(unescaped, "|c", "")
    local _, colorEnds = string.gsub(unescaped, "|r", "")
    if colorStarts > colorEnds then
        result = result .. "|r"
    end
    return result
end

-- Default operational presets
local DEFAULT_PRESETS = {
    raffle_push = {
        name = "Weekly Raffle Push",
        text = "|cFFD700★ {guild_name} WEEKLY RAFFLE ({raffle_dates}) ★|r\nPot: currently at |cFFD700{raffle_pot}|r |t16:16:EsoUI/Art/currency/currency_gold.dds|t\nPool: |c00FFCCtickets in pool|r |c00FFCC{raffle_tickets}|r |t16:16:EsoUI/Art/icons/quest_ticket.dds|t\nParticipants: |cFFFFFFentrants|r |cFFFFFF{raffle_entrants}|r |t16:16:EsoUI/Art/compass/compass_groupLeader.dds|t\n1st: |cFFD700{raffle_first}|r | 2nd: |cFFAA00{raffle_second}|r | 3rd: |cFF8800{raffle_third}|r\nDeposit 1,000g in guild bank for 1 ticket! Drawing {drawing_date}.\nKiosk: |c59E08A{kiosk_location}|r",
    },
    winners = {
        name = "Winners Announcement",
        text = "|cFFD700★ {guild_name} RAFFLE WINNERS ({raffle_prev_dates}) ★|r\nTotal Pot: |cFFD700{raffle_pot}|r gold!\n{raffle_winners}\nCongratulations! Gold has been dispatched by staff courier.\nNext week's raffle is now LIVE ({raffle_dates}). Good luck!",
    },
    trader_update = {
        name = "Trader Kiosk Notice",
        text = "|c00FFCC★ {guild_name} TRADER UPDATE ★|r\nCurrent Kiosk: |c59E08A{kiosk_location}|r\nAll store sales and bank deposits directly fund our weekly trader bid!\nKeep listings stocked with 30 items. Thank you for your support!",
    }
}

--[[ =========================================================================
     MOTD TEMPLATE PERSISTENCE (Solving the MotD Template Paradox - Fable 5.1)
========================================================================= ]]--

function FR:EnsureMotDState()
    if not self.savedVars then return end
    if not self.savedVars.motdTemplates then
        self.savedVars.motdTemplates = {}
    end
end

function FR:GetGuildMotDTemplate(guildId)
    self:EnsureMotDState()
    guildId = self:ResolveGuildId(guildId or self.selectedGuildIndex or 1)
    if self.savedVars and self.savedVars.motdTemplates and self.savedVars.motdTemplates[guildId] then
        local tmpl = self.savedVars.motdTemplates[guildId]
        if tmpl and tmpl ~= "" then
            return tmpl
        end
    end
    -- Fallback to default weekly raffle push template
    return DEFAULT_PRESETS and DEFAULT_PRESETS.raffle_push and DEFAULT_PRESETS.raffle_push.text or ""
end

function FR:SetGuildMotDTemplate(guildId, templateText)
    self:EnsureMotDState()
    guildId = self:ResolveGuildId(guildId or self.selectedGuildIndex or 1)
    if self.savedVars and self.savedVars.motdTemplates then
        self.savedVars.motdTemplates[guildId] = templateText
    end
end

--[[ =========================================================================
     RAFFLE DATE & CYCLE CALCULATOR
========================================================================= ]]--

function FR:GetRaffleDateInfo(guildId)
    local nowTs = GetTimeStamp()
    local curStart = self.GetCurrentRaffleWeekStart and self:GetCurrentRaffleWeekStart(nowTs) or (1789945200 + math.floor((nowTs - 1789945200) / 604800) * 604800)
    local curEnd = curStart + 604800
    local prevStart = curStart - 604800

    local function SafeDate(fmt, ts)
        if os and os.date then
            local ok, str = pcall(os.date, fmt, ts)
            if ok and str then return str end
        end
        return ""
    end

    local startMonth = SafeDate("%b", curStart)
    local startDay = tonumber(SafeDate("%d", curStart)) or ""
    local endMonth = SafeDate("%b", curEnd)
    local endDay = tonumber(SafeDate("%d", curEnd)) or ""

    local curRange
    if startMonth ~= "" and endMonth ~= "" then
        if startMonth == endMonth then
            curRange = string.format("%s %s – %s", startMonth, tostring(startDay), tostring(endDay))
        else
            curRange = string.format("%s %s – %s %s", startMonth, tostring(startDay), endMonth, tostring(endDay))
        end
    else
        curRange = "Active Cycle"
    end

    local prevMonth = SafeDate("%b", prevStart)
    local prevDay = tonumber(SafeDate("%d", prevStart)) or ""
    local prevRange
    if prevMonth ~= "" and startMonth ~= "" then
        if prevMonth == startMonth then
            prevRange = string.format("%s %s – %s", prevMonth, tostring(prevDay), tostring(startDay))
        else
            prevRange = string.format("%s %s – %s %s", prevMonth, tostring(prevDay), startMonth, tostring(startDay))
        end
    else
        prevRange = "Prior Week"
    end

    local drawWeekday = SafeDate("%A", curEnd)
    if drawWeekday == "" then drawWeekday = "Sunday" end
    local drawingStr = (endMonth ~= "" and endDay ~= "")
        and string.format("%s, %s %s (7:00 PM ET)", drawWeekday, endMonth, tostring(endDay))
        or string.format("%s (7:00 PM ET)", drawWeekday)

    -- Check sealed data for specific guild
    local guildName = guildId and GetGuildName(guildId) or ""
    local isPost = string.find(guildName, "Post") ~= nil
    local isDealers = string.find(guildName, "Dealer") ~= nil
    local isCaravan = string.find(guildName, "Caravan") ~= nil
    local gKey = isPost and "post" or (isDealers and "dealers" or (isCaravan and "caravan" or nil))
    local raffleData = gKey and self.GetRaffleData and self:GetRaffleData(gKey)
    local sealedLabel = raffleData and raffleData.weekLabel or (FR.OfficialRaffleLedger and FR.OfficialRaffleLedger.weekLabel)

    return {
        currentRange = curRange,
        previousRange = sealedLabel or prevRange,
        startDate = (startMonth ~= "" and startDay ~= "") and string.format("%s %s", startMonth, tostring(startDay)) or "Start",
        endDate = (endMonth ~= "" and endDay ~= "") and string.format("%s %s", endMonth, tostring(endDay)) or "End",
        drawingDate = drawingStr,
        drawDay = drawWeekday,
        sealedLabel = sealedLabel,
    }
end

--[[ =========================================================================
     TOKEN RESOLUTION ENGINE & SURGICAL REPLACERS
========================================================================= ]]--

function FR:ReplaceMotDDateRange(text, dateInfo)
    if not text or text == "" or not dateInfo then return text end

    local isWinnerAnnouncement = string.find(string.lower(text), "winner") ~= nil
    local targetRange = isWinnerAnnouncement and dateInfo.previousRange or dateInfo.currentRange
    local resolved = text

    local months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" }
    local lowerText = string.lower(resolved)

    -- 1. Surgical Legacy Date Range (e.g. "Sep 13 - 20", "Sep 13 - Sep 20", "Sep 13 – 20")
    for _, m in ipairs(months) do
        local lowerM = string.lower(m)
        local pos = 1
        while true do
            local startIdx = string.find(lowerText, lowerM, pos, true)
            if not startIdx then break end

            local remainder = resolved:sub(startIdx)
            local fullMatch = remainder:match("^([A-Za-z]+%s+%d+%s*[%-–—]%s*[A-Za-z]+%s+%d+)")
            if not fullMatch then
                fullMatch = remainder:match("^([A-Za-z]+%s+%d+%s*[%-–—]%s*%d+)")
            end

            if fullMatch and #fullMatch > 0 then
                local endIdx = startIdx + #fullMatch - 1
                resolved = resolved:sub(1, startIdx - 1) .. targetRange .. resolved:sub(endIdx + 1)
                break
            end

            pos = startIdx + #lowerM
        end
    end

    -- 2. Surgical Legacy Drawing Date (e.g. "Drawing Sunday, Sep 20" or "Drawing Sep 20")
    if not isWinnerAnnouncement and dateInfo.drawingDate and dateInfo.drawingDate ~= "" then
        for _, m in ipairs(months) do
            local pattern = "([Dd]rawing%s+[%a,]-%s*" .. m .. "%s+%d+)"
            local s, e, matchStr = resolved:find(pattern)
            if s and matchStr then
                resolved = resolved:sub(1, s - 1) .. "Drawing " .. dateInfo.drawingDate .. resolved:sub(e + 1)
                break
            end
        end
    end

    return resolved
end

function FR:ResolveMotDTokens(rawText, guildId)
    if not rawText or rawText == "" then return "" end
    guildId = guildId or GetGuildId(self.selectedGuildIndex or 1)
    local guildName = GetGuildName(guildId)

    local isPost = string.find(guildName, "Post") ~= nil
    local isDealers = string.find(guildName, "Dealer") ~= nil
    local isCaravan = string.find(guildName, "Caravan") ~= nil
    local gKey = isPost and "post" or (isDealers and "dealers" or (isCaravan and "caravan" or nil))
    local raffleData = gKey and self.GetRaffleData and self:GetRaffleData(gKey)
    local liveMetrics = self.CalculateRaffleMetrics and self:CalculateRaffleMetrics(guildId, 7, 1000)
    local dateInfo = self:GetRaffleDateInfo(guildId)

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

    -- Prioritize live bank ledger if active deposits are present; fallback to sealed draw data
    local pot = (liveMetrics and liveMetrics.totalGold > 0) and liveMetrics.totalGold or (raffleData and raffleData.pot or 0)
    local tickets = (liveMetrics and liveMetrics.totalGold > 0) and liveMetrics.totalTickets or (raffleData and raffleData.tickets or 0)
    local entrants = (liveMetrics and liveMetrics.totalGold > 0) and liveMetrics.entrants or (raffleData and raffleData.entrants or 0)
    local entries = (liveMetrics and liveMetrics.totalGold > 0) and liveMetrics.entries or 0

    local potStr = FormatGoldVal(pot)
    local tixStr = FormatGoldVal(tickets)
    local entStr = tostring(entrants)
    local entriesStr = tostring(entries)

    local firstPrize = (liveMetrics and liveMetrics.totalGold > 0) and math.floor(pot * 0.30) or (raffleData and raffleData.prizes and raffleData.prizes.first or 0)
    local secondPrize = (liveMetrics and liveMetrics.totalGold > 0) and math.floor(pot * 0.20) or (raffleData and raffleData.prizes and raffleData.prizes.second or 0)
    local thirdPrize = (liveMetrics and liveMetrics.totalGold > 0) and math.floor(pot * 0.10) or (raffleData and raffleData.prizes and raffleData.prizes.third or 0)

    local firstStr = FormatGoldVal(firstPrize)
    local secondStr = FormatGoldVal(secondPrize)
    local thirdStr = FormatGoldVal(thirdPrize)
    local prizesStr = string.format("1st: %s | 2nd: %s | 3rd: %s", firstStr, secondStr, thirdStr)

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
        ["{raffle_entries}"] = entriesStr,
        ["{raffle_prizes}"] = prizesStr,
        ["{raffle_first}"] = firstStr,
        ["{raffle_second}"] = secondStr,
        ["{raffle_third}"] = thirdStr,
        ["{raffle_winners}"] = winnersStr,
        ["{kiosk_location}"] = kioskStr,
        ["{guild_name}"] = guildName,
        ["{raffle_dates}"] = dateInfo.currentRange,
        ["{raffle_prev_dates}"] = dateInfo.previousRange,
        ["{drawing_date}"] = dateInfo.drawingDate,
        ["{raffle_start}"] = dateInfo.startDate,
        ["{raffle_end}"] = dateInfo.endDate,
        ["{date_week}"] = dateInfo.sealedLabel or dateInfo.currentRange,
    }

    local resolved = rawText
    for token, val in pairs(tokens) do
        -- Escape magic patterns in search string
        local pat = string.gsub(token, "([%(%)%.%%%+%-%*%?%[%]%^%$])", "%%%1")
        -- Pass function replacer so % in replacement value is never treated as pattern capture
        resolved = string.gsub(resolved, pat, function() return val end)
    end

    -- Hybrid In-Place Update: If legacy static raffle values exist in the text
    -- (e.g. 'tickets in pool 5420', 'currently at 542,4000', 'entrants 250', 'entries 26'),
    -- surgically replace them with the active guild's live metrics.
    if self.ReplaceRaffleField then
        resolved = self:ReplaceRaffleField(resolved, "currently at", potStr)
        resolved = self:ReplaceRaffleField(resolved, "tickets in pool", tixStr)
        resolved = self:ReplaceRaffleField(resolved, "entrants", entStr)
        resolved = self:ReplaceRaffleField(resolved, "entries", entriesStr)
    end

    -- Surgical Legacy Date Range & Drawing Replacement:
    -- Automatically advance static legacy dates (e.g. "Sep 13 - 20") to the active cycle ("Sep 20 – 27").
    if self.ReplaceMotDDateRange then
        resolved = self:ReplaceMotDDateRange(resolved, dateInfo)
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

    -- 3. Token Quick-Insert Chips Bar (Dual-Tier Flow Layout)
    -- Row 1: Financial & Raffle Ledger Tokens
    local chipBarLbl1 = wm:CreateControl("$(parent)_ChipLbl1", card, CT_LABEL)
    chipBarLbl1:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 33)
    chipBarLbl1:SetFont("ZoFontGameSmall")
    chipBarLbl1:SetText("|c888888Raffle:|r")

    local chipsRow1 = {
        { label = "+ Pot", token = "{raffle_pot}", width = 66, tip = "Live total gold in raffle pot." },
        { label = "+ Tickets", token = "{raffle_tickets}", width = 74, tip = "Total tickets purchased in current cycle." },
        { label = "+ Entrants", token = "{raffle_entrants}", width = 78, tip = "Count of unique participating ticket holders." },
        { label = "+ Prizes", token = "{raffle_prizes}", width = 72, tip = "Calculated prize breakdown: 1st, 2nd, and 3rd." },
        { label = "+ 1st", token = "{raffle_first}", width = 54, tip = "1st place prize amount." },
        { label = "+ 2nd", token = "{raffle_second}", width = 54, tip = "2nd place prize amount." },
        { label = "+ 3rd", token = "{raffle_third}", width = 54, tip = "3rd place prize amount." },
        { label = "+ Winners", token = "{raffle_winners}", width = 80, tip = "Names and ticket numbers of prior week winners." },
    }

    local xOff1 = 66
    for idx, c in ipairs(chipsRow1) do
        local cBtn = wm:CreateControl("$(parent)_Chip1_" .. idx, card, CT_BUTTON)
        cBtn:SetAnchor(TOPLEFT, card, TOPLEFT, xOff1, 31)
        cBtn:SetDimensions(c.width, 22)
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
            tooltipText = c.tip,
        })
        cBtn:SetHandler("OnClicked", function()
            self:InsertTokenIntoMotDEditBox(c.token)
        end)
        xOff1 = xOff1 + c.width + 5
    end

    -- Row 2: Date Bounds & Guild Operations Tokens
    local chipBarLbl2 = wm:CreateControl("$(parent)_ChipLbl2", card, CT_LABEL)
    chipBarLbl2:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 59)
    chipBarLbl2:SetFont("ZoFontGameSmall")
    chipBarLbl2:SetText("|c888888Dates & Info:|r")

    local chipsRow2 = {
        { label = "+ Dates", token = "{raffle_dates}", width = 76, tip = "Active weekly raffle date range (e.g. Sep 20 – Sep 27)." },
        { label = "+ Drawing", token = "{drawing_date}", width = 84, tip = "Next drawing deadline timestamp (e.g. Sunday, Sep 27 (7:00 PM ET))." },
        { label = "+ Prior Week", token = "{raffle_prev_dates}", width = 90, tip = "Prior sealed raffle week range (e.g. Sep 13 - Sep 20)." },
        { label = "+ Start", token = "{raffle_start}", width = 68, tip = "Start date of current raffle cycle (e.g. Sep 20)." },
        { label = "+ End", token = "{raffle_end}", width = 68, tip = "End date of current raffle cycle (e.g. Sep 27)." },
        { label = "+ Kiosk", token = "{kiosk_location}", width = 74, tip = "Current trader kiosk location." },
        { label = "+ Guild", token = "{guild_name}", width = 72, tip = "Name of active guild." },
    }

    local xOff2 = 98
    for idx, c in ipairs(chipsRow2) do
        local cBtn = wm:CreateControl("$(parent)_Chip2_" .. idx, card, CT_BUTTON)
        cBtn:SetAnchor(TOPLEFT, card, TOPLEFT, xOff2, 57)
        cBtn:SetDimensions(c.width, 22)
        cBtn:SetFont("ZoFontGameSmall")
        cBtn:SetText(c.label)
        self:StyleTactileButton(cBtn, {
            normalBg = { 0.08, 0.08, 0.04, 0.85 },
            hoverBg = { 0.14, 0.14, 0.06, 0.95 },
            normalEdge = { 0.65, 0.55, 0.15, 0.65 },
            hoverEdge = { 1.00, 0.85, 0.25, 1.00 },
            normalTextColor = { 1, 0.90, 0.35, 1 },
            hoverTextColor = { 1, 1, 0.70, 1 },
            tooltipTitle = "Insert Token: " .. c.token,
            tooltipText = c.tip,
        })
        cBtn:SetHandler("OnClicked", function()
            self:InsertTokenIntoMotDEditBox(c.token)
        end)
        xOff2 = xOff2 + c.width + 6
    end

    -- 4. Multi-line EditBox Container (Comfortably seated below dual-row chip bar)
    local editBg = wm:CreateControlFromVirtual("$(parent)_EditBackdrop", card, "ZO_EditBackdrop")
    editBg:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 86)
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
    gaugeLbl:SetText(string.format("Length: 0 / %d characters (0 bytes)", MAX_MOTD_CHARS))
    self.motdStudioGaugeLbl = gaugeLbl

    -- 6. Preset Selector Buttons (Dedicated Mid Tier)
    local presetLbl = wm:CreateControl("$(parent)_PresetLbl", card, CT_LABEL)
    presetLbl:SetAnchor(TOPLEFT, editBg, BOTTOMLEFT, 4, 30)
    presetLbl:SetFont("ZoFontGameSmall")
    presetLbl:SetText("|c888888Load Template:|r")

    local pBtn1 = wm:CreateControl("$(parent)_PresetRaffle", card, CT_BUTTON)
    pBtn1:SetAnchor(LEFT, presetLbl, RIGHT, 8, 0)
    pBtn1:SetDimensions(100, 24)
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
    pBtn2:SetDimensions(95, 24)
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
    pBtn3:SetDimensions(95, 24)
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
    clearBtn:SetAnchor(TOPRIGHT, editBg, BOTTOMRIGHT, -4, 28)
    clearBtn:SetDimensions(95, 24)
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

    -- 7. Action Bar (Bottom Row - Anchored along unified baseline with explicit dimensions)
    local interpolateBtn = wm:CreateControl("$(parent)_InterpolateBtn", card, CT_BUTTON)
    interpolateBtn:SetAnchor(BOTTOMLEFT, card, BOTTOMLEFT, 12, -10)
    interpolateBtn:SetDimensions(155, 28)
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
        tooltipText = "Replace all {tokens} in the editor with live data (pot gold, dates, kiosk location, winners) from the guild ledger.",
    })
    interpolateBtn:SetHandler("OnClicked", function()
        self:ApplyTokensToMotDEditor()
    end)

    local previewBtn = wm:CreateControl("$(parent)_PreviewBtn", card, CT_BUTTON)
    previewBtn:SetAnchor(BOTTOMLEFT, interpolateBtn, BOTTOMRIGHT, 10, 0)
    previewBtn:SetDimensions(135, 28)
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
    revertBtn:SetAnchor(BOTTOMLEFT, previewBtn, BOTTOMRIGHT, 10, 0)
    revertBtn:SetDimensions(135, 28)
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
    pushBtn:SetAnchor(BOTTOMLEFT, revertBtn, BOTTOMRIGHT, 10, 0)
    pushBtn:SetAnchor(BOTTOMRIGHT, card, BOTTOMRIGHT, -12, -10)
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
    elseif charCount > (MAX_MOTD_CHARS - 100) then
        colorCode = "FFCC00"
        statusNote = string.format("|cFFCC00%d characters remaining (Near limit)|r", MAX_MOTD_CHARS - charCount)
    end

    self.motdStudioGaugeLbl:SetText(string.format("Length: |c%s%d / %d characters|r (|c888888%s bytes|r) • %s",
        colorCode, charCount, MAX_MOTD_CHARS, ZO_LocalizeDecimalNumber(byteCount), statusNote))
end

function FR:InsertTokenIntoMotDEditBox(token)
    if not self.motdStudioEditBox then return end

    if self.motdStudioEditBox.InsertText then
        self.motdStudioEditBox:InsertText(token)
    else
        local cur = self.motdStudioEditBox:GetText() or ""
        local pos = self.motdStudioEditBox:GetCursorPosition() or #cur

        local before = string.sub(cur, 1, pos)
        local after = string.sub(cur, pos + 1)
        local newText = before .. token .. after

        self.motdStudioEditBox:SetText(newText)
        self.motdStudioEditBox:SetCursorPosition(pos + #token)
    end

    if self.motdStudioEditBox.TakeFocus then
        self.motdStudioEditBox:TakeFocus()
    end

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
    -- If raw text contains template tokens, persist as the guild's active template (Fable 5.1)
    if string.find(raw, "{raffle_") or string.find(raw, "{guild_") or string.find(raw, "{drawing_date}") or string.find(raw, "{date_week}") then
        self:SetGuildMotDTemplate(guildId, raw)
    end
    local resolved = self:ResolveMotDTokens(raw, guildId)

    -- Auto-balance unclosed |c color tags before broadcast
    local _, colorStarts = string.gsub(resolved, "|c", "")
    local _, colorEnds = string.gsub(resolved, "|r", "")
    if colorStarts > colorEnds then
        resolved = resolved .. string.rep("|r", colorStarts - colorEnds)
    end

    local charCount = (zo_strlen and zo_strlen(resolved)) or #resolved
    local byteCount = #resolved

    if charCount > MAX_MOTD_CHARS or byteCount > MAX_MOTD_CHARS then
        self.PrintChat(string.format("|cFF5555Error:|r MotD exceeds the limit (%d chars, %d bytes, max %d). Please shorten before pushing.", charCount, byteCount, MAX_MOTD_CHARS))
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
