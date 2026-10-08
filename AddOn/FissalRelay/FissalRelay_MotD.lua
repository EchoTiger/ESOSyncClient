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

local MONTH_NAMES = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" }
local DAYS_IN_MONTH = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 }

local function FallbackDate(ts)
    local days = math.floor(ts / 86400)
    local weekday = ((days + 4) % 7) -- 0=Sun, 1=Mon, ..., 6=Sat
    local DAY_NAMES = { [0] = "Sunday", [1] = "Monday", [2] = "Tuesday", [3] = "Wednesday", [4] = "Thursday", [5] = "Friday", [6] = "Saturday" }

    local y = 1970
    while true do
        local leap = (y % 4 == 0 and (y % 100 ~= 0 or y % 400 == 0))
        local daysInYear = leap and 366 or 365
        if days < daysInYear then break end
        days = days - daysInYear
        y = y + 1
    end
    local leap = (y % 4 == 0 and (y % 100 ~= 0 or y % 400 == 0))
    local m = 1
    while m <= 12 do
        local dim = DAYS_IN_MONTH[m]
        if m == 2 and leap then dim = 29 end
        if days < dim then break end
        days = days - dim
        m = m + 1
    end
    local d = days + 1
    return MONTH_NAMES[m] or "Sep", d, DAY_NAMES[weekday] or "Sunday", y
end

function FR:NormalizeDashesBytePreserving(str)
    if not str then return "" end
    -- Replace multi-byte UTF-8 dashes with equal-length ASCII hyphens so byte indices match 1:1
    local out = str:gsub("\xE2\x80\x93", "---"):gsub("\xE2\x80\x94", "---"):gsub("\xE2\x80\x92", "---"):gsub("\xE2\x80\x91", "---")
    out = out:gsub("\xC2\xA0", "  ")
    return out
end

function FR:GetRaffleDateInfo(guildId)
    local nowTs = GetTimeStamp()
    local curStart = self.GetCurrentRaffleWeekStart and self:GetCurrentRaffleWeekStart(nowTs) or (1789945200 + math.floor((nowTs - 1789945200) / 604800) * 604800)
    local curEnd = curStart + 604800
    local prevStart = curStart - 604800

    local function SafeDate(fmt, ts)
        if os and os.date then
            -- Align to Eastern Time (EDT = UTC-4 = 14400s)
            local ok, str = pcall(os.date, "!" .. fmt, ts - 14400)
            if ok and str and str ~= "" then return str end
            local ok2, str2 = pcall(os.date, fmt, ts)
            if ok2 and str2 and str2 ~= "" then return str2 end
        end
        local fMonth, fDay, fWeekday = FallbackDate(ts - 14400)
        if fmt == "%b" then return fMonth
        elseif fmt == "%d" then return tostring(fDay)
        elseif fmt == "%A" then return fWeekday
        end
        return ""
    end

    local startMonth = SafeDate("%b", curStart)
    local startDay = tonumber(SafeDate("%d", curStart)) or ""
    local endMonth = SafeDate("%b", curEnd)
    local endDay = tonumber(SafeDate("%d", curEnd)) or ""

    local curRange
    if startMonth ~= "" and endMonth ~= "" then
        curRange = string.format("%s %s – %s %s", startMonth, tostring(startDay), endMonth, tostring(endDay))
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

    local prevPrevStart = prevStart - 604800
    local prevPrevMonth = SafeDate("%b", prevPrevStart)
    local prevPrevDay = tonumber(SafeDate("%d", prevPrevStart)) or ""
    local prevPrevRange
    if prevPrevMonth ~= "" and prevMonth ~= "" then
        if prevPrevMonth == prevMonth then
            prevPrevRange = string.format("%s %s – %s", prevPrevMonth, tostring(prevPrevDay), tostring(prevDay))
        else
            prevPrevRange = string.format("%s %s – %s %s", prevPrevMonth, tostring(prevPrevDay), prevMonth, tostring(prevDay))
        end
    else
        prevPrevRange = "Archived Cycle"
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
    local sealedData = (FR.OfficialRaffleLedger and gKey and FR.OfficialRaffleLedger[gKey])
        or (self.savedVars and self.savedVars.raffleData and gKey and self.savedVars.raffleData[gKey])
        or (DEFAULT_RAFFLE_CACHE and gKey and DEFAULT_RAFFLE_CACHE[gKey])
    local sealedLabel = sealedData and (sealedData.weekLabel or sealedData.weekStart)
        or (FR.OfficialRaffleLedger and FR.OfficialRaffleLedger.weekLabel)

    return {
        currentRange = curRange,
        previousRange = sealedLabel or prevRange,
        prevPrevRange = prevPrevRange,
        startDate = (startMonth ~= "" and startDay ~= "") and string.format("%s %s", startMonth, tostring(startDay)) or "Start",
        endDate = (endMonth ~= "" and endDay ~= "") and string.format("%s %s", endMonth, tostring(endDay)) or "End",
        drawingDate = drawingStr,
        drawDay = drawWeekday,
        sealedLabel = sealedLabel,
        guildKey = gKey,
        sealedData = sealedData,
    }
end

--[[ =========================================================================
     TOKEN RESOLUTION ENGINE & SURGICAL REPLACERS
========================================================================= ]]--

function FR:ReplaceMotDDateRange(text, dateInfo)
    if not text or text == "" or not dateInfo then return text end

    -- Accurate Winner Announcement check: Only if the title/header explicitly announces winners,
    -- NOT just because the word "winner" or "winners" appears in the body (e.g. "Winners drawn Sunday" or "{raffle_winners}").
    local lowerText = string.lower(text)
    local isWinnerAnnouncement = false
    if (lowerText:find("raffle winner") or lowerText:find("weekly winner") or lowerText:find("winners announcement") or lowerText:find("congratulations"))
       and not (lowerText:find("weekly raffle") or lowerText:find("raffle push")) then
        isWinnerAnnouncement = true
    end

    local cycle = self.selectedMotDWeekCycle or "live"
    local targetRange
    if cycle == "last" or (cycle == "live" and isWinnerAnnouncement) then
        targetRange = dateInfo.previousRange
    elseif cycle == "prev" then
        targetRange = dateInfo.prevPrevRange or dateInfo.previousRange
    else
        targetRange = dateInfo.currentRange
    end

    local resolved = text
    local norm = self.NormalizeDashesBytePreserving and self:NormalizeDashesBytePreserving(resolved)
        or resolved:gsub("\xE2\x80\x93", "---"):gsub("\xE2\x80\x94", "---")

    -- Priority 1: Full Month Day - Month Day (e.g. "Oct 4 - Oct 11", "Oct 4 – Oct 11", "Sep 27 to Oct 4")
    -- Separators: hyphens/slashes or 'to' (Strictly no '~' or '@' so we never eat approximation prefixes or times)
    local patA1 = "([A-Za-z]+%.?%s+%d%d?%a*%s*[%-%/]+%s*[A-Za-z]+%.?%s+%d%d?%a*)"
    local patA2 = "([A-Za-z]+%.?%s+%d%d?%a*%s+to%s+[A-Za-z]+%.?%s+%d%d?%a*)"

    local s, e = norm:find(patA1)
    if not s then s, e = norm:find(patA2) end

    -- Guard: reject match if it ends with 'pm' or 'am'
    if s and e then
        local match = norm:sub(s, e):lower()
        if match:match("%d+pm$") or match:match("%d+am$") then
            s, e = nil, nil
        end
    end

    -- Priority 2: Same-month Month Day - Day (e.g. "Oct 4 - 11", "Oct 4 to 11")
    -- CRITICAL: Ensure the second number is NOT a time like '7pm' or '7:00'
    if not s then
        local patB1 = "([A-Za-z]+%.?%s+%d%d?%a*%s*[%-%/]+%s*(%d%d?)(%a*))"
        local patB2 = "([A-Za-z]+%.?%s+%d%d?%a*%s+to%s+(%d%d?)(%a*))"
        for _, pat in ipairs({ patB1, patB2 }) do
            local curS, curE, fullM, dayNum, suffix = norm:find(pat)
            if curS then
                suffix = suffix:lower()
                local isOrd = (suffix == "" or suffix == "st" or suffix == "nd" or suffix == "rd" or suffix == "th")
                local nextChar = norm:sub(curE + 1, curE + 1)
                local isTime = (suffix == "pm" or suffix == "am" or nextChar == ":" or nextChar:lower() == "p" or nextChar:lower() == "a")
                if isOrd and not isTime then
                    s, e = curS, curE
                    break
                end
            end
        end
    end

    -- Priority 3: Numeric M/D - M/D (e.g. "9/27 - 10/4" or "10/4 - 10/11")
    if not s then
        local patC = "(%d%d?/%d%d?%s*[%-%/]+%s*%d%d?/%d%d?)"
        s, e = norm:find(patC)
    end

    if s and e and targetRange and targetRange ~= "" then
        resolved = resolved:sub(1, s - 1) .. targetRange .. resolved:sub(e + 1)
    end

    -- 2. Surgical Legacy Drawing Date (e.g. "Drawing Sunday, Sep 27 (7:00 PM ET)" or "Drawing Oct 11 ~7pm ET" or "Drawing Oct 11 @ 7pm ET")
    if not isWinnerAnnouncement and dateInfo.drawingDate and dateInfo.drawingDate ~= "" then
        local normDraw = self.NormalizeDashesBytePreserving and self:NormalizeDashesBytePreserving(resolved)
            or resolved:gsub("\xE2\x80\x93", "---"):gsub("\xE2\x80\x94", "---")
        -- Match Drawing with optional colon, weekday, month, day, and flexible time spec (~7pm, @ 7:00 PM ET, (7:00 PM ET), etc.)
        local drawPat = "([Dd]rawing:?%s+[%a,]-%s*[A-Za-z]+%.?%s+%d%d?%a*%s*[%(@~%s%d:%a%-]*%a*%)?)"
        local ds, de, matchStr = normDraw:find(drawPat)
        if ds and de then
            local trailingPeriod = matchStr:match("%.%s*$") and "." or ""
            resolved = resolved:sub(1, ds - 1) .. "Drawing " .. dateInfo.drawingDate .. trailingPeriod .. resolved:sub(de + 1)
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

    -- Dynamic cycle selection (live bank, last sealed draw, or previous cycle)
    local cycle = self.selectedMotDWeekCycle or "live"
    local pot, tickets, entrants, entries, dateWeekStr

    if cycle == "last" then
        pot = raffleData and raffleData.pot or 0
        tickets = raffleData and raffleData.tickets or 0
        entrants = raffleData and raffleData.entrants or 0
        entries = entrants
        dateWeekStr = dateInfo.previousRange
    elseif cycle == "prev" then
        pot = raffleData and raffleData.pot or 0
        tickets = raffleData and raffleData.tickets or 0
        entrants = raffleData and raffleData.entrants or 0
        entries = entrants
        dateWeekStr = dateInfo.prevPrevRange or dateInfo.previousRange
    else -- "live"
        local isLiveBank = (liveMetrics and liveMetrics.totalGold > 0)
        pot = isLiveBank and liveMetrics.totalGold or (raffleData and raffleData.pot or 0)
        tickets = isLiveBank and liveMetrics.totalTickets or (raffleData and raffleData.tickets or 0)
        entrants = isLiveBank and liveMetrics.entrants or (raffleData and raffleData.entrants or 0)
        entries = isLiveBank and liveMetrics.entries or 0
        dateWeekStr = dateInfo.currentRange
    end

    local potStr = FormatGoldVal(pot)
    local tixStr = FormatGoldVal(tickets)
    local entStr = tostring(entrants)
    local entriesStr = tostring(entries)

    local firstPrize = math.floor(pot * 0.30)
    local secondPrize = math.floor(pot * 0.20)
    local thirdPrize = math.floor(pot * 0.10)
    if cycle ~= "live" and raffleData and raffleData.prizes then
        firstPrize = raffleData.prizes.first or firstPrize
        secondPrize = raffleData.prizes.second or secondPrize
        thirdPrize = raffleData.prizes.third or thirdPrize
    end

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
        ["{raffle_dates}"] = dateWeekStr,
        ["{raffle_prev_dates}"] = dateInfo.previousRange,
        ["{drawing_date}"] = (cycle == "live") and dateInfo.drawingDate or (cycle == "last" and "Concluded" or "Archived"),
        ["{raffle_start}"] = dateInfo.startDate,
        ["{raffle_end}"] = dateInfo.endDate,
        ["{date_week}"] = dateWeekStr,
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
    title:SetText("|cFF9900MOTD BROADCAST STUDIO|r")

    local sourceBadge = wm:CreateControl("$(parent)_SourceBadge", card, CT_LABEL)
    sourceBadge:SetAnchor(LEFT, title, RIGHT, 10, 0)
    sourceBadge:SetFont("ZoFontGameBold")
    sourceBadge:SetText("|c59E08A[SYNCED]|r")
    self.motdSourceBadge = sourceBadge

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
    editBg:SetAnchor(BOTTOMRIGHT, card, BOTTOMRIGHT, -12, -108)

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
    gaugeLbl:SetAnchor(TOPLEFT, editBg, BOTTOMLEFT, 4, 4)
    gaugeLbl:SetAnchor(TOPRIGHT, editBg, BOTTOMRIGHT, -4, 4)
    gaugeLbl:SetFont("ZoFontGameBold")
    gaugeLbl:SetText(string.format("Length: 0 / %d characters (0 bytes)", MAX_MOTD_CHARS))
    self.motdStudioGaugeLbl = gaugeLbl

    -- 5b. Active Ledger & Token Telemetry Ribbon (Single Bounded Line with Tooltip)
    local telemetryBar = wm:CreateControl("$(parent)_TelemetryBar", card, CT_LABEL)
    telemetryBar:SetAnchor(TOPLEFT, editBg, BOTTOMLEFT, 4, 24)
    telemetryBar:SetAnchor(TOPRIGHT, editBg, BOTTOMRIGHT, -4, 24)
    telemetryBar:SetHeight(20)
    telemetryBar:SetMaxLineCount(1)
    telemetryBar:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
    telemetryBar:SetFont("ZoFontGameSmall")
    telemetryBar:SetText("|c888888Active Ledger Telemetry: Initializing...|r")
    telemetryBar:SetMouseEnabled(true)
    telemetryBar:SetHandler("OnMouseEnter", function(ctrl)
        if ctrl.tooltipData then
            InitializeTooltip(InformationTooltip, ctrl, TOP, 0, -4)
            local tip = string.format("|cFF9900%s|r\n|cCCCCCC%s|r", ctrl.tooltipData.title, ctrl.tooltipData.text)
            SetTooltipText(InformationTooltip, tip)
        end
    end)
    telemetryBar:SetHandler("OnMouseExit", function()
        ClearTooltip(InformationTooltip)
    end)
    self.motdTelemetryBar = telemetryBar

    -- 6. Preset Selector Buttons & Raffle Cycle Dropdown (Dedicated Mid Tier)
    local presetLbl = wm:CreateControl("$(parent)_PresetLbl", card, CT_LABEL)
    presetLbl:SetAnchor(TOPLEFT, editBg, BOTTOMLEFT, 4, 48)
    presetLbl:SetFont("ZoFontGameSmall")
    presetLbl:SetText("|c888888Template:|r")

    local pBtn1 = wm:CreateControl("$(parent)_PresetRaffle", card, CT_BUTTON)
    pBtn1:SetAnchor(LEFT, presetLbl, RIGHT, 8, 0)
    pBtn1:SetDimensions(95, 24)
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
    pBtn2:SetAnchor(LEFT, pBtn1, RIGHT, 6, 0)
    pBtn2:SetDimensions(85, 24)
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
    pBtn3:SetAnchor(LEFT, pBtn2, RIGHT, 6, 0)
    pBtn3:SetDimensions(85, 24)
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

    -- Raffle Week Selection Dropdown
    local weekLbl = wm:CreateControl("$(parent)_WeekLbl", card, CT_LABEL)
    weekLbl:SetAnchor(LEFT, pBtn3, RIGHT, 12, 0)
    weekLbl:SetFont("ZoFontGameSmall")
    weekLbl:SetText("|c888888Cycle:|r")

    local weekDropdownCtrl = wm:CreateControlFromVirtual("$(parent)_WeekDropdown", card, "ZO_ComboBox")
    weekDropdownCtrl:SetAnchor(LEFT, weekLbl, RIGHT, 6, 0)
    weekDropdownCtrl:SetDimensions(190, 24)
    local weekCb = ZO_ComboBox_ObjectFromContainer(weekDropdownCtrl)
    weekCb:SetSortsItems(false)
    weekCb:SetSpacing(4)
    self.motdWeekComboBox = weekCb

    local function OnWeekSelected(_, entryText, entry)
        self.selectedMotDWeekCycle = entry.mode or "live"
        self:UpdateMotDTelemetryBar()
        self:UpdateMotDStudioGauge()
    end

    local eLive = weekCb:CreateItemEntry("Live Raffle (Active Bank)", OnWeekSelected)
    eLive.mode = "live"
    weekCb:AddItem(eLive)

    local eLast = weekCb:CreateItemEntry("Last Raffle (Sealed Draw)", OnWeekSelected)
    eLast.mode = "last"
    weekCb:AddItem(eLast)

    local ePrev = weekCb:CreateItemEntry("Previous Raffle (Archive)", OnWeekSelected)
    ePrev.mode = "prev"
    weekCb:AddItem(ePrev)

    weekCb:SelectItem(eLive)
    self.selectedMotDWeekCycle = "live"

    local undoBtn = wm:CreateControl("$(parent)_UndoBtn", card, CT_BUTTON)
    undoBtn:SetAnchor(TOPRIGHT, editBg, BOTTOMRIGHT, -170, 48)
    undoBtn:SetDimensions(62, 24)
    undoBtn:SetFont("ZoFontGameSmall")
    undoBtn:SetText("↶ Undo")
    self:StyleTactileButton(undoBtn, {
        normalBg = { 0.08, 0.08, 0.12, 0.90 },
        hoverBg = { 0.12, 0.12, 0.18, 0.98 },
        normalEdge = { 0.40, 0.40, 0.70, 0.80 },
        hoverEdge = { 0.60, 0.60, 1.00, 1.00 },
        normalTextColor = { 0.8, 0.8, 1, 1 },
        hoverTextColor = { 1, 1, 1, 1 },
        tooltipTitle = "Undo Last Action",
        tooltipText = "Restore previous draft state before clearing, loading presets, or editing.",
    })
    undoBtn:SetHandler("OnClicked", function()
        self:UndoMotD()
    end)

    local redoBtn = wm:CreateControl("$(parent)_RedoBtn", card, CT_BUTTON)
    redoBtn:SetAnchor(TOPRIGHT, editBg, BOTTOMRIGHT, -104, 48)
    redoBtn:SetDimensions(62, 24)
    redoBtn:SetFont("ZoFontGameSmall")
    redoBtn:SetText("Redo ↷")
    self:StyleTactileButton(redoBtn, {
        normalBg = { 0.08, 0.08, 0.12, 0.90 },
        hoverBg = { 0.12, 0.12, 0.18, 0.98 },
        normalEdge = { 0.40, 0.40, 0.70, 0.80 },
        hoverEdge = { 0.60, 0.60, 1.00, 1.00 },
        normalTextColor = { 0.8, 0.8, 1, 1 },
        hoverTextColor = { 1, 1, 1, 1 },
        tooltipTitle = "Redo Action",
        tooltipText = "Reapply the undone action in the MotD Studio editor.",
    })
    redoBtn:SetHandler("OnClicked", function()
        self:RedoMotD()
    end)

    local clearBtn = wm:CreateControl("$(parent)_ClearBtn", card, CT_BUTTON)
    clearBtn:SetAnchor(TOPRIGHT, editBg, BOTTOMRIGHT, -4, 48)
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
        tooltipText = "Clear all contents currently inside the MotD editor box (saved to Undo buffer).",
    })
    clearBtn:SetHandler("OnClicked", function()
        if self.motdStudioEditBox then
            local cur = self.motdStudioEditBox:GetText() or ""
            if cur ~= "" then
                self:PushMotDUndoState(cur)
                self.motdStudioEditBox:SetText("")
                self:UpdateMotDStudioGauge()
                self.PrintChat("Editor text cleared. (Click [Undo] anytime to restore!)")
            end
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
     MOTD STUDIO CONTROLS & ACTIONS (WITH UNDO/REDO & CONFIRMATION)
========================================================================= ]]--

FR.motdUndoStack = {}
FR.motdRedoStack = {}

function FR:PushMotDUndoState(text)
    text = text or (self.motdStudioEditBox and self.motdStudioEditBox:GetText()) or ""
    local top = self.motdUndoStack[#self.motdUndoStack]
    if top == text then return end
    table.insert(self.motdUndoStack, text)
    if #self.motdUndoStack > 50 then
        table.remove(self.motdUndoStack, 1)
    end
    self.motdRedoStack = {}
end

function FR:UndoMotD()
    if not self.motdStudioEditBox then return end
    if #self.motdUndoStack == 0 then
        self.PrintChat("Nothing to undo in MotD Studio.")
        return
    end
    local cur = self.motdStudioEditBox:GetText() or ""
    table.insert(self.motdRedoStack, cur)
    local prev = table.remove(self.motdUndoStack)
    self.motdStudioEditBox:SetText(prev)
    self:UpdateMotDStudioGauge()
    self.PrintChat("MotD action undone.")
end

function FR:RedoMotD()
    if not self.motdStudioEditBox then return end
    if #self.motdRedoStack == 0 then
        self.PrintChat("Nothing to redo in MotD Studio.")
        return
    end
    local cur = self.motdStudioEditBox:GetText() or ""
    table.insert(self.motdUndoStack, cur)
    local nxt = table.remove(self.motdRedoStack)
    self.motdStudioEditBox:SetText(nxt)
    self:UpdateMotDStudioGauge()
    self.PrintChat("MotD action redone.")
end

function FR:UpdateMotDTelemetryBar(guildId)
    if not self.motdTelemetryBar and not self.motdSourceBadge then return end
    guildId = guildId or GetGuildId(self.selectedGuildIndex or 1)
    local guildName = GetGuildName(guildId)
    local isPost = string.find(guildName, "Post") ~= nil
    local isDealers = string.find(guildName, "Dealer") ~= nil
    local isCaravan = string.find(guildName, "Caravan") ~= nil
    local gKey = isPost and "post" or (isDealers and "dealers" or (isCaravan and "caravan" or nil))

    local raffleData = gKey and self.GetRaffleData and self:GetRaffleData(gKey)
    local liveMetrics = self.CalculateRaffleMetrics and self:CalculateRaffleMetrics(guildId, 7, 1000)
    local dateInfo = self:GetRaffleDateInfo(guildId)

    local cycle = self.selectedMotDWeekCycle or "live"
    local pot, tickets, entrants, rangeStr, drawStr, badgeText, winnersTooltip

    local FormatGold = function(n)
        return ZO_LocalizeDecimalNumber and ZO_LocalizeDecimalNumber(n or 0) or tostring(n or 0)
    end

    if cycle == "last" then
        badgeText = "|c59E08A[SEALED DRAW]|r"
        pot = raffleData and raffleData.pot or 0
        tickets = raffleData and raffleData.tickets or 0
        entrants = raffleData and raffleData.entrants or 0
        rangeStr = dateInfo.previousRange or "Prior Cycle"
        drawStr = "Concluded"
    elseif cycle == "prev" then
        badgeText = "|c00FFCC[ARCHIVED]|r"
        pot = raffleData and raffleData.pot or 0
        tickets = raffleData and raffleData.tickets or 0
        entrants = raffleData and raffleData.entrants or 0
        rangeStr = dateInfo.prevPrevRange or "Archived Cycle"
        drawStr = "Archived"
    else -- "live"
        local isLiveBank = (liveMetrics and liveMetrics.totalGold > 0)
        badgeText = isLiveBank and "|cFFD700[LIVE BANK]|r" or "|c59E08A[DISCORD SYNCED]|r"
        pot = isLiveBank and liveMetrics.totalGold or (raffleData and raffleData.pot or 0)
        tickets = isLiveBank and liveMetrics.totalTickets or (raffleData and raffleData.tickets or 0)
        entrants = isLiveBank and liveMetrics.entrants or (raffleData and raffleData.entrants or 0)
        rangeStr = dateInfo.currentRange or "Active Cycle"
        drawStr = dateInfo.drawingDate or "Sunday 7:00 PM ET"
    end

    if raffleData and raffleData.winners and #raffleData.winners > 0 then
        local wParts = {}
        for _, w in ipairs(raffleData.winners) do
            table.insert(wParts, string.format("• #%d %s (%s gold)", w.place or 1, tostring(w.name or "@winner"), FormatGold(w.prize)))
        end
        winnersTooltip = table.concat(wParts, "\n")
    else
        winnersTooltip = "No winners recorded."
    end

    if self.motdTelemetryBar then
        local text = string.format("%s |c00FFCC%s|r • %s • Pot: |cFFD700%sg|r (%s tix, %s entrants)",
            badgeText, guildName ~= "" and guildName or "Guild", rangeStr, FormatGold(pot), FormatGold(tickets), tostring(entrants))
        self.motdTelemetryBar:SetText(text)

        self.motdTelemetryBar.tooltipData = {
            title = string.format("%s Telemetry (%s)", guildName ~= "" and guildName or "Guild", rangeStr),
            text = string.format("Cycle Mode: %s\nPot: %s gold\nTickets: %s (%s unique entrants)\nDrawing Deadline: %s\n\nWinners:\n%s",
                badgeText, FormatGold(pot), FormatGold(tickets), tostring(entrants), drawStr, winnersTooltip),
        }
    end

    if self.motdSourceBadge then
        self.motdSourceBadge:SetText(badgeText)
    end
end

function FR:UpdateMotDStudioGauge()
    if not self.motdStudioEditBox or not self.motdStudioGaugeLbl then return end
    local text = self.motdStudioEditBox:GetText() or ""
    local charCount = (zo_strlen and zo_strlen(text)) or #text
    local byteCount = #text
    local hasTokens = string.find(text, "{") ~= nil

    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)
    local resChars = charCount
    local resBytes = byteCount
    if hasTokens then
        local resolved = self:ResolveMotDTokens(text, guildId)
        resChars = (zo_strlen and zo_strlen(resolved)) or #resolved
        resBytes = #resolved
    end

    local colorCode = "59E08A"
    local statusNote = string.format("%d characters remaining", MAX_MOTD_CHARS - resChars)

    if resChars > MAX_MOTD_CHARS then
        colorCode = "FF5555"
        statusNote = string.format("|cFF5555+%d OVER limit!|r", resChars - MAX_MOTD_CHARS)
    elseif resChars > (MAX_MOTD_CHARS - 100) then
        colorCode = "FFCC00"
        statusNote = string.format("|cFFCC00%d characters remaining (Near limit)|r", MAX_MOTD_CHARS - resChars)
    end

    if hasTokens then
        local tagStatus = (charCount > MAX_MOTD_CHARS and resChars <= MAX_MOTD_CHARS)
            and "|c59E08A● Tags resolve within limit!|r"
            or (resChars <= MAX_MOTD_CHARS and "|c59E08A● Ready to interpolate|r" or "|cFF5555▲ Exceeds limit after tags|r")
        self.motdStudioGaugeLbl:SetText(string.format("Draft: |c888888%d chars|r • Live Push: |c%s%d / %d chars|r (%s) • %s",
            charCount, colorCode, resChars, MAX_MOTD_CHARS, statusNote, tagStatus))
    else
        self.motdStudioGaugeLbl:SetText(string.format("Length: |c%s%d / %d characters|r (|c888888%s bytes|r) • %s",
            colorCode, charCount, MAX_MOTD_CHARS, ZO_LocalizeDecimalNumber(byteCount), statusNote))
    end

    self:UpdateMotDTelemetryBar(guildId)
end

function FR:InsertTokenIntoMotDEditBox(token)
    if not self.motdStudioEditBox then return end
    local cur = self.motdStudioEditBox:GetText() or ""
    self:PushMotDUndoState(cur)

    if self.motdStudioEditBox.InsertText then
        self.motdStudioEditBox:InsertText(token)
    else
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
    local cur = self.motdStudioEditBox:GetText() or ""
    if cur ~= "" and cur ~= preset.text then
        self:PushMotDUndoState(cur)
    end
    self.motdStudioEditBox:SetText(preset.text)
    self:UpdateMotDStudioGauge()
    self.PrintChat(string.format("Loaded MotD template: '%s'. (Click [Undo] to restore prior draft)", preset.name))
end

function FR:ApplyTokensToMotDEditor()
    if not self.motdStudioEditBox then return end
    local raw = self.motdStudioEditBox:GetText() or ""
    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)

    self:PushMotDUndoState(raw)
    local resolved = self:ResolveMotDTokens(raw, guildId)
    self.motdStudioEditBox:SetText(resolved)
    self:UpdateMotDStudioGauge()
    self.PrintChat("Live guild tokens interpolated into editor. (Click [Undo] anytime to revert)")
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

function FR:OpenMotDPushModal(guildId, guildName, resolved, charCount, byteCount)
    local wm = WINDOW_MANAGER
    if not self.motdPushModal then
        local modal = wm:CreateTopLevelWindow("FissalRelay_MotDPushModal")
        modal:SetDimensions(720, 520)
        modal:SetAnchor(CENTER, GuiRoot, CENTER, 0, -20)
        modal:SetClampedToScreen(true)
        modal:SetMouseEnabled(true)
        modal:SetMovable(true)

        if UISpecialWindows then
            table.insert(UISpecialWindows, "FissalRelay_MotDPushModal")
        end

        local bg = wm:CreateControl("$(parent)_Bg", modal, CT_BACKDROP)
        bg:SetAnchorFill()
        bg:SetCenterColor(0.04, 0.04, 0.06, 1.0)
        bg:SetEdgeColor(0.95, 0.70, 0.20, 0.95)
        bg:SetEdgeTexture("", 8, 1, 0)
        bg:SetDrawLayer(DL_BACKGROUND)
        bg:SetDrawLevel(0)

        local defBg = wm:CreateControlFromVirtual("$(parent)_DefBg", modal, "ZO_DefaultBackdrop")
        defBg:SetAnchorFill()
        defBg:SetAlpha(1.0)
        defBg:SetDrawLayer(DL_BACKGROUND)
        defBg:SetDrawLevel(1)

        local munge = wm:CreateControl("$(parent)_Munge", modal, CT_TEXTURE)
        munge:SetAnchorFill()
        munge:SetTexture("EsoUI/Art/Performance/StatusMeterMunge.dds")
        munge:SetColor(0.04, 0.04, 0.06, 0.98)
        munge:SetDrawLayer(DL_BACKGROUND)
        munge:SetDrawLevel(2)

        -- Title & Close Button
        local titleLbl = wm:CreateControl("$(parent)_Title", modal, CT_LABEL)
        titleLbl:SetAnchor(TOPLEFT, modal, TOPLEFT, 16, 12)
        titleLbl:SetFont("ZoFontGameBold")
        titleLbl:SetText("|cFF9900PUSH MESSAGE OF THE DAY LIVE|r  •  |c00FFCCPre-Flight Verification|r")

        local closeBtn = wm:CreateControl("$(parent)_CloseBtn", modal, CT_BUTTON)
        closeBtn:SetAnchor(TOPRIGHT, modal, TOPRIGHT, -12, 10)
        closeBtn:SetDimensions(26, 26)
        closeBtn:SetFont("ZoFontGameBold")
        closeBtn:SetText("X")
        self:StyleTactileButton(closeBtn, {
            normalBg = { 0.15, 0.05, 0.05, 0.85 },
            hoverBg = { 0.30, 0.08, 0.08, 0.95 },
            normalEdge = { 0.60, 0.20, 0.20, 0.80 },
            hoverEdge = { 1.00, 0.30, 0.30, 1.00 },
            normalTextColor = { 1, 0.5, 0.5, 1 },
        })
        closeBtn:SetHandler("OnClicked", function()
            modal:SetHidden(true)
        end)

        local subLbl = wm:CreateControl("$(parent)_Sub", modal, CT_LABEL)
        subLbl:SetAnchor(TOPLEFT, titleLbl, BOTTOMLEFT, 0, 4)
        subLbl:SetFont("ZoFontGameSmall")
        subLbl:SetText("Carefully verify the live preview below. Dynamic tokens are interpolated with live ledger figures.")

        -- Info Ribbon
        local ribbon = wm:CreateControl("$(parent)_Ribbon", modal, CT_BACKDROP)
        ribbon:SetAnchor(TOPLEFT, modal, TOPLEFT, 16, 56)
        ribbon:SetAnchor(TOPRIGHT, modal, TOPRIGHT, -16, 56)
        ribbon:SetHeight(28)
        ribbon:SetCenterColor(0.06, 0.06, 0.09, 0.85)
        ribbon:SetEdgeColor(0.20, 0.20, 0.25, 0.60)
        ribbon:SetEdgeTexture("", 8, 1, 0)

        local targetGuildLbl = wm:CreateControl("$(parent)_Target", ribbon, CT_LABEL)
        targetGuildLbl:SetAnchor(LEFT, ribbon, LEFT, 10, 0)
        targetGuildLbl:SetFont("ZoFontGameBold")
        targetGuildLbl:SetText("Guild: --")

        local charStatLbl = wm:CreateControl("$(parent)_CharStat", ribbon, CT_LABEL)
        charStatLbl:SetAnchor(RIGHT, ribbon, RIGHT, -10, 0)
        charStatLbl:SetFont("ZoFontGameSmall")
        charStatLbl:SetText("Length: 0 / 1024 chars")

        -- Scrollable Preview Box
        local previewBox = wm:CreateControl("$(parent)_PreviewBox", modal, CT_BACKDROP)
        previewBox:SetAnchor(TOPLEFT, ribbon, BOTTOMLEFT, 0, 8)
        previewBox:SetAnchor(BOTTOMRIGHT, modal, BOTTOMRIGHT, -16, -48)
        previewBox:SetCenterColor(0.03, 0.03, 0.04, 0.90)
        previewBox:SetEdgeColor(0.25, 0.25, 0.30, 0.70)
        previewBox:SetEdgeTexture("", 8, 1, 0)

        local scrollContainer = wm:CreateControlFromVirtual("$(parent)_Scroll", previewBox, "ZO_ScrollContainer")
        scrollContainer:SetAnchor(TOPLEFT, previewBox, TOPLEFT, 8, 8)
        scrollContainer:SetAnchor(BOTTOMRIGHT, previewBox, BOTTOMRIGHT, -8, -8)

        local scrollChild = scrollContainer.scrollChild or GetControl(scrollContainer, "ScrollChild")
        local previewLbl = wm:CreateControl("$(parent)_PreviewText", scrollChild, CT_LABEL)
        previewLbl:ClearAnchors()
        previewLbl:SetAnchor(TOPLEFT, scrollChild, TOPLEFT, 6, 6)
        previewLbl:SetWidth(654)
        previewLbl:SetFont("ZoFontGame")
        previewLbl:SetColor(1, 1, 1, 1)

        -- Footer Controls
        local warningLbl = wm:CreateControl("$(parent)_Warn", modal, CT_LABEL)
        warningLbl:SetAnchor(BOTTOMLEFT, modal, BOTTOMLEFT, 16, -14)
        warningLbl:SetFont("ZoFontGameSmall")
        warningLbl:SetText("|cAAAAAA⚠ Broadcasts immediately to all guild members.|r")

        local cancelBtn = wm:CreateControl("$(parent)_CancelBtn", modal, CT_BUTTON)
        cancelBtn:SetAnchor(BOTTOMRIGHT, modal, BOTTOMRIGHT, -200, -10)
        cancelBtn:SetDimensions(90, 28)
        cancelBtn:SetFont("ZoFontGameBold")
        cancelBtn:SetText("Cancel")
        self:StyleTactileButton(cancelBtn, {
            normalBg = { 0.12, 0.12, 0.15, 0.85 },
            hoverBg = { 0.18, 0.18, 0.22, 0.95 },
            normalEdge = { 0.30, 0.30, 0.35, 0.70 },
        })
        cancelBtn:SetHandler("OnClicked", function()
            modal:SetHidden(true)
        end)

        local confirmBtn = wm:CreateControl("$(parent)_ConfirmBtn", modal, CT_BUTTON)
        confirmBtn:SetAnchor(BOTTOMRIGHT, modal, BOTTOMRIGHT, -16, -10)
        confirmBtn:SetDimensions(175, 28)
        confirmBtn:SetFont("ZoFontGameBold")
        confirmBtn:SetText("✓ Confirm & Push Live")
        self:StyleTactileButton(confirmBtn, {
            normalBg = { 0.04, 0.18, 0.10, 0.95 },
            hoverBg = { 0.06, 0.26, 0.15, 1.00 },
            normalEdge = { 0.20, 0.95, 0.45, 0.95 },
            hoverEdge = { 0.30, 1.00, 0.55, 1.00 },
            normalTextColor = { 0.2, 1, 0.5, 1 },
        })

        modal.scrollContainer = scrollContainer
        modal.targetGuildLbl = targetGuildLbl
        modal.charStatLbl = charStatLbl
        modal.previewLbl = previewLbl
        modal.confirmBtn = confirmBtn
        self.motdPushModal = modal
    end

    local modal = self.motdPushModal
    modal.targetGuildLbl:SetText(string.format("Target Guild: |c00FFCC%s|r", guildName))
    modal.charStatLbl:SetText(string.format("Length: |c%s%d / 1024 chars|r  (|cAAAAAA%d bytes|r)",
        charCount > 1000 and "FF5555" or "59E08A", charCount, byteCount))
    modal.previewLbl:SetText(resolved)
    if modal.scrollContainer and ZO_Scroll_ResetToTop then
        ZO_Scroll_ResetToTop(modal.scrollContainer)
    end

    modal.confirmBtn:SetHandler("OnClicked", function()
        modal:SetHidden(true)
        FR:BroadcastMotDToGuild(true, guildId)
    end)

    modal:SetHidden(false)
end

function FR:BroadcastMotDToGuild(bypassConfirm, targetGuildId)
    if not self.motdStudioEditBox then return end
    local gIdx = self.motdSelectedGuildIndex or self.selectedGuildIndex or 1
    local guildId = targetGuildId or GetGuildId(gIdx)
    local guildName = GetGuildName(guildId)

    if not DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_SET_MOTD) then
        self.PrintChat(string.format("|cFF5555Permission Denied:|r You do not have permission to change the MotD in %s.", guildName))
        return
    end

    local raw = self.motdStudioEditBox:GetText() or ""
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

    if not bypassConfirm then
        self:OpenMotDPushModal(guildId, guildName, resolved, charCount, byteCount)
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
