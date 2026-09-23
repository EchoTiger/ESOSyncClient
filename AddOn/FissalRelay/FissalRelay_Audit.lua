--[[
    FissalRelay_Audit.lua
    Inactivity & Dues Auditor for Fissal Relay Prime
    Crafted by Echo & Fissal for Fissal Relay and the Redfur Guilds.

    Features:
      • High-performance roster auditing across all 5 guilds (up to 500 members each)
      • Smart exclusion filters:
          - Days offline thresholds (7d, 14d, 21d, 30d)
          - Officer / Leader rank immunity toggle
          - Excused / LOA detection in member notes ([LOA], [E], break, vacation)
          - Live text search filter by @handle or note keywords
      • Paginated data grid with smooth page controls (10 rows per page, zero frame drops)
      • Itemized bank deposits cross-referenced against LibHistoire banked currency records
      • 1-Click "Warn Mail" handoff directly to Fissal's Mail Assistant
      • Purge list export to chat and SavedVariables for Discord reporting
]]--

FissalRelay = FissalRelay or {}
local FR = FissalRelay

local function ColorText(text, hexColor)
    return string.format("|c%s%s|r", hexColor, tostring(text or ""))
end

local ROWS_PER_PAGE = 9

FR.auditFilterDays = 14
FR.auditExcludeOfficers = true
FR.auditExcludeLOA = true
FR.auditShieldActiveSellers = true
FR.auditRankFilter = "all"
FR.auditSortBy = "days"
FR.auditSortAsc = false
FR.auditSearchQuery = ""
FR.auditCurrentPage = 1
FR.auditFilteredMembers = {}

--[[ =========================================================================
     AUDITOR DATA ENGINE
========================================================================= ]]--

-- Memoized bank deposits per guild
FR.depositCache = {}

function FR:InvalidateDepositCache(guildId)
    if guildId then
        self.depositCache[guildId] = nil
    else
        self.depositCache = {}
    end
end

function FR:GetMemberDeposits(guildId)
    if self.depositCache[guildId] then
        return self.depositCache[guildId]
    end

    local lookup = {}
    if self.savedVars and self.savedVars.staff and self.savedVars.staff.bankDeposits then
        for _, dep in pairs(self.savedVars.staff.bankDeposits) do
            if dep.guildId == guildId and dep.depositor then
                local lowerDep = string.lower(dep.depositor)
                lookup[lowerDep] = (lookup[lowerDep] or 0) + (dep.amount or 0)
            end
        end
    end
    self.depositCache[guildId] = lookup
    return lookup
end

function FR:RunRosterAudit()
    local gIdx = self.selectedGuildIndex or 1
    local numGuilds = GetNumGuilds()
    if gIdx < 1 or gIdx > numGuilds then return end

    local guildId = GetGuildId(gIdx)
    local guildName = GetGuildName(guildId)
    local memberCount = GetNumGuildMembers(guildId)
    local cutoffSecs = (self.auditFilterDays or 14) * 86400

    local duesRule = self.GetRedfurDuesRule and self:GetRedfurDuesRule(guildId) or { windowDays = 10 }
    local scanWindowDays = math.max(self.auditFilterDays or 14, duesRule.windowDays or 10)

    -- Retrieve memoized deposit lookup for this guild (case-insensitive)
    local depositsByMember = self:GetMemberDeposits(guildId)
    -- Retrieve member sales lookup for this guild (case-insensitive)
    local salesByMember = self.GetMemberSales and self:GetMemberSales(guildId, scanWindowDays) or {}

    local rawInactives = {}
    local excusedCount = 0
    local officerCount = 0
    local shieldedSellerCount = 0

    for m = 1, memberCount do
        local name, note, rankIndex, playerStatus, secsSinceLogoff = GetGuildMemberInfo(guildId, m)
        local isOffline = (playerStatus == PLAYER_STATUS_OFFLINE) or (secsSinceLogoff and secsSinceLogoff > 0)

        if isOffline and secsSinceLogoff and secsSinceLogoff >= cutoffSecs then
            local days = math.floor(secsSinceLogoff / 86400)
            local rankName = GetGuildRankCustomName(guildId, rankIndex)
            local isLeaderOrOfficer = (rankIndex and rankIndex <= 2)

            local rawName = name or ""
            local cleanName = (rawName:sub(1,1) == "@") and rawName:sub(2) or rawName
            local lowerName = string.lower(cleanName)

            -- Sales & Deposits
            local sRec = salesByMember[lowerName] or { count = 0, gold = 0, lastSaleTs = 0 }
            local mDeposits = depositsByMember[lowerName] or 0
            local duesMet, duesReason = false, ""
            if self.EvaluateRedfurDues then
                duesMet, duesReason = self:EvaluateRedfurDues(guildId, name, mDeposits, sRec.count, sRec.gold)
            else
                duesMet = (sRec.count > 0 or mDeposits > 0)
                duesReason = duesMet and "Active" or "Unmet"
            end

            -- Check LOA / Excused
            local noteLower = string.lower(note or "")
            local isLOA = string.find(noteLower, "%[loa%]") or string.find(noteLower, "%[e%]")
                or string.find(noteLower, "loa") or string.find(noteLower, "excuse")
                or string.find(noteLower, "break") or string.find(noteLower, "vacation")

            if isLeaderOrOfficer then officerCount = officerCount + 1 end
            if isLOA then excusedCount = excusedCount + 1 end

            local passFilter = true
            if self.auditExcludeOfficers and isLeaderOrOfficer then
                passFilter = false
            end
            if self.auditExcludeLOA and isLOA then
                passFilter = false
            end

            -- Invisible Player Protection: Shield active sellers from purge
            if self.auditShieldActiveSellers and (sRec.count > 0 or duesMet) then
                shieldedSellerCount = shieldedSellerCount + 1
                passFilter = false
            end

            -- Rank Filter (e.g. "all" or specific rank name or index)
            if self.auditRankFilter and self.auditRankFilter ~= "all" then
                local rankMatches = (tostring(rankIndex) == tostring(self.auditRankFilter)) or
                                    (string.lower(rankName or "") == string.lower(self.auditRankFilter))
                if not rankMatches then
                    passFilter = false
                end
            end

            if passFilter then
                -- Text search filter
                if self.auditSearchQuery and self.auditSearchQuery ~= "" then
                    local q = string.lower(self.auditSearchQuery)
                    local nameMatch = string.find(string.lower(name or ""), q, 1, true)
                    local noteMatch = string.find(noteLower, q, 1, true)
                    if not nameMatch and not noteMatch then
                        passFilter = false
                    end
                end
            end

            if passFilter then
                local cleanRank = (rankName and rankName ~= "") and rankName or string.format("Rank %d", rankIndex or 0)
                table.insert(rawInactives, {
                    name = name or "@Unknown",
                    note = note or "",
                    rank = cleanRank,
                    rankIndex = rankIndex or 0,
                    days = days,
                    isLOA = isLOA,
                    isOfficer = isLeaderOrOfficer,
                    deposits = mDeposits,
                    salesCount = sRec.count,
                    salesGold = sRec.gold,
                    duesMet = duesMet,
                    duesReason = duesReason,
                })
            end
        end
    end

    -- Flexible multi-column sorting
    table.sort(rawInactives, function(a, b)
        local sortBy = self.auditSortBy or "days"
        local asc = self.auditSortAsc or false

        if sortBy == "rank" then
            if a.rankIndex ~= b.rankIndex then
                return asc and (a.rankIndex < b.rankIndex) or (a.rankIndex > b.rankIndex)
            end
            return a.days > b.days
        elseif sortBy == "sales" then
            if a.salesCount ~= b.salesCount then
                return asc and (a.salesCount < b.salesCount) or (a.salesCount > b.salesCount)
            end
            return a.salesGold > b.salesGold
        elseif sortBy == "dues" then
            if a.duesMet ~= b.duesMet then
                return asc and (a.duesMet and not b.duesMet) or (not a.duesMet and b.duesMet)
            end
            return a.deposits < b.deposits
        elseif sortBy == "name" then
            return asc and (a.name < b.name) or (a.name > b.name)
        else -- "days"
            return asc and (a.days < b.days) or (a.days > b.days)
        end
    end)

    self.auditFilteredMembers = rawInactives
    self.auditTotalMembers = memberCount
    self.auditExcusedCount = excusedCount
    self.auditOfficerCount = officerCount
    self.auditShieldedCount = shieldedSellerCount

    -- Save structured audit snapshot
    if self.savedVars and self.savedVars.staff then
        if not self.savedVars.staff.inactivityAudits then
            self.savedVars.staff.inactivityAudits = {}
        end
        self.savedVars.staff.inactivityAudits[tostring(guildId)] = {
            guildId = guildId,
            guildName = guildName,
            auditedAt = GetTimeStamp(),
            minDays = self.auditFilterDays,
            totalMembers = memberCount,
            inactiveCount = #rawInactives,
            shieldedSellers = shieldedSellerCount,
            members = rawInactives,
        }
    end

    local maxPages = math.max(1, math.ceil(#rawInactives / ROWS_PER_PAGE))
    if self.auditCurrentPage > maxPages then
        self.auditCurrentPage = maxPages
    end
end

--[[ =========================================================================
     AUDITOR UI CONSTRUCTION
========================================================================= ]]--

function FR:BuildAuditorUI(parent)
    local wm = WINDOW_MANAGER
    local panel = wm:CreateControl("FissalRelay_Console_Tab3", parent, CT_CONTROL)
    panel:SetAnchorFill()
    panel:SetHidden(true)
    self.consoleTabs[3] = panel

    -- 1. Backdrop Card
    local card = wm:CreateControl("$(parent)_Card", panel, CT_BACKDROP)
    card:SetAnchorFill()
    card:SetCenterColor(0.06, 0.06, 0.08, 0.85)
    card:SetEdgeColor(0.30, 0.25, 0.18, 0.70)
    card:SetEdgeTexture("", 8, 1, 0)

    -- 2. Filter Bar (Top Row)
    local daysLbl = wm:CreateControl("$(parent)_DaysLbl", card, CT_LABEL)
    daysLbl:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 10)
    daysLbl:SetFont("ZoFontGameSmall")
    daysLbl:SetText("|c888888Min Offline:|r")

    self.auditDayBtns = {}
    local dayOptions = {
        { days = 7, tip = "Show members offline for 7 or more days." },
        { days = 14, tip = "Show members offline for 14 or more days (standard check-in threshold)." },
        { days = 21, tip = "Show members offline for 21 or more days." },
        { days = 30, tip = "Show members offline for 30 or more days (standard purge threshold)." },
    }
    for idx, opt in ipairs(dayOptions) do
        local d = opt.days
        local btn = wm:CreateControl("$(parent)_DayBtn_" .. d, card, CT_BUTTON)
        btn:SetAnchor(TOPLEFT, card, TOPLEFT, 85 + (idx - 1) * 44, 7)
        btn:SetDimensions(40, 22)
        btn:SetFont("ZoFontGameSmall")
        btn:SetText(string.format("%dd", d))
        self:StyleTactileButton(btn, {
            normalBg = { 0.06, 0.06, 0.08, 0.80 },
            hoverBg = { 0.14, 0.12, 0.06, 0.95 },
            normalEdge = { 0.35, 0.28, 0.18, 0.60 },
            hoverEdge = { 0.90, 0.70, 0.20, 1.0 },
            normalTextColor = { 0.7, 0.7, 0.7, 1 },
            hoverTextColor = { 1, 0.9, 0.4, 1 },
            tooltipTitle = string.format("Filter: %d+ Days Offline", d),
            tooltipText = opt.tip,
        })
        btn:SetHandler("OnClicked", function()
            self.auditFilterDays = d
            self.auditCurrentPage = 1
            self:UpdateAuditorUI()
        end)
        self.auditDayBtns[d] = btn
    end

    -- Toggle: Exclude Officers
    local offToggle = wm:CreateControl("$(parent)_OffToggle", card, CT_BUTTON)
    offToggle:SetAnchor(TOPLEFT, card, TOPLEFT, 210, 7)
    offToggle:SetDimensions(90, 22)
    offToggle:SetFont("ZoFontGameSmall")
    offToggle:SetText("No Officers")
    self:StyleTactileButton(offToggle, {
        normalBg = { 0.04, 0.12, 0.08, 0.85 },
        hoverBg = { 0.06, 0.18, 0.12, 0.95 },
        normalEdge = { 0.20, 0.75, 0.35, 0.80 },
        hoverEdge = { 0.30, 1.00, 0.50, 1.00 },
        normalTextColor = { 0.3, 1, 0.5, 1 },
        hoverTextColor = { 0.6, 1, 0.7, 1 },
        tooltipTitle = "Exclude Officers",
        tooltipText = "Hide Guild Master and Officer ranks (Ranks 1 & 2) from purge list.",
    })
    offToggle:SetHandler("OnClicked", function()
        self.auditExcludeOfficers = not self.auditExcludeOfficers
        self.auditCurrentPage = 1
        self:UpdateAuditorUI()
    end)
    self.auditOffToggle = offToggle

    -- Toggle: Exclude LOA
    local loaToggle = wm:CreateControl("$(parent)_LoaToggle", card, CT_BUTTON)
    loaToggle:SetAnchor(TOPLEFT, card, TOPLEFT, 305, 7)
    loaToggle:SetDimensions(85, 22)
    loaToggle:SetFont("ZoFontGameSmall")
    loaToggle:SetText("No [LOA]")
    self:StyleTactileButton(loaToggle, {
        normalBg = { 0.04, 0.12, 0.08, 0.85 },
        hoverBg = { 0.06, 0.18, 0.12, 0.95 },
        normalEdge = { 0.20, 0.75, 0.35, 0.80 },
        hoverEdge = { 0.30, 1.00, 0.50, 1.00 },
        normalTextColor = { 0.3, 1, 0.5, 1 },
        hoverTextColor = { 0.6, 1, 0.7, 1 },
        tooltipTitle = "Exclude [LOA]",
        tooltipText = "Hide members whose member notes contain [LOA], [E], break, or vacation tags.",
    })
    loaToggle:SetHandler("OnClicked", function()
        self.auditExcludeLOA = not self.auditExcludeLOA
        self.auditCurrentPage = 1
        self:UpdateAuditorUI()
    end)
    self.auditLoaToggle = loaToggle

    -- Toggle: Shield Active Sellers (Invisible Players)
    local shieldToggle = wm:CreateControl("$(parent)_ShieldToggle", card, CT_BUTTON)
    shieldToggle:SetAnchor(TOPLEFT, card, TOPLEFT, 395, 7)
    shieldToggle:SetDimensions(115, 22)
    shieldToggle:SetFont("ZoFontGameSmall")
    shieldToggle:SetText("Shield Sellers")
    self:StyleTactileButton(shieldToggle, {
        normalBg = { 0.04, 0.12, 0.14, 0.85 },
        hoverBg = { 0.06, 0.18, 0.20, 0.95 },
        normalEdge = { 0.0, 0.75, 0.85, 0.80 },
        hoverEdge = { 0.0, 1.00, 0.95, 1.00 },
        normalTextColor = { 0, 1, 0.9, 1 },
        hoverTextColor = { 0.4, 1, 1, 1 },
        tooltipTitle = "Shield Active Sellers (Invisible Players)",
        tooltipText = "Do not flag or kick players who appear offline if they have actively made sales or met guild dues in the active window.",
    })
    shieldToggle:SetHandler("OnClicked", function()
        self.auditShieldActiveSellers = not self.auditShieldActiveSellers
        self.auditCurrentPage = 1
        self:UpdateAuditorUI()
    end)
    self.auditShieldToggle = shieldToggle

    -- Rank Filter Cycle Button
    local rankBtn = wm:CreateControl("$(parent)_RankFilterBtn", card, CT_BUTTON)
    rankBtn:SetAnchor(TOPLEFT, card, TOPLEFT, 515, 7)
    rankBtn:SetDimensions(105, 22)
    rankBtn:SetFont("ZoFontGameSmall")
    rankBtn:SetText("Rank: All")
    self:StyleTactileButton(rankBtn, {
        normalBg = { 0.10, 0.08, 0.14, 0.85 },
        hoverBg = { 0.16, 0.12, 0.22, 0.95 },
        normalEdge = { 0.60, 0.40, 0.85, 0.80 },
        hoverEdge = { 0.80, 0.50, 1.00, 1.00 },
        normalTextColor = { 0.85, 0.70, 1, 1 },
        hoverTextColor = { 1, 0.85, 1, 1 },
        tooltipTitle = "Rank Filter",
        tooltipText = "Filter table by specific guild rank (click to cycle through ranks).",
    })
    rankBtn:SetHandler("OnClicked", function()
        self:CycleAuditRankFilter()
    end)
    self.auditRankFilterBtn = rankBtn

    -- Search Box Container
    local searchBg = wm:CreateControlFromVirtual("$(parent)_SearchBg", card, "ZO_EditBackdrop")
    searchBg:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 6)
    searchBg:SetDimensions(135, 24)

    local searchBox = wm:CreateControlFromVirtual("$(parent)_Search", searchBg, "ZO_DefaultEditForBackdrop")
    searchBox:SetAnchorFill()
    searchBox:SetFont("ZoFontGameSmall")
    searchBox:SetTextType(TEXT_TYPE_ALL)
    searchBox:SetDefaultText("Search...")
    searchBox:SetHandler("OnTextChanged", function(ctrl)
        self.auditSearchQuery = ctrl:GetText()
        self.auditCurrentPage = 1
        EVENT_MANAGER:UnregisterForUpdate("FissalRelay_AuditSearchDebounce")
        EVENT_MANAGER:RegisterForUpdate("FissalRelay_AuditSearchDebounce", 250, function()
            EVENT_MANAGER:UnregisterForUpdate("FissalRelay_AuditSearchDebounce")
            self:UpdateAuditorUI()
        end)
    end)
    self.auditSearchBox = searchBox

    -- 3. Table Column Headers
    local headerY = 36
    local colHeader = wm:CreateControl("$(parent)_Header", card, CT_BACKDROP)
    colHeader:SetAnchor(TOPLEFT, card, TOPLEFT, 10, headerY)
    colHeader:SetAnchor(TOPRIGHT, card, TOPRIGHT, -10, headerY)
    colHeader:SetHeight(22)
    colHeader:SetCenterColor(0.10, 0.10, 0.14, 0.90)
    colHeader:SetEdgeColor(0.25, 0.20, 0.15, 0.60)
    colHeader:SetEdgeTexture("", 8, 1, 0)

    local h1 = wm:CreateControl("$(parent)_H1", colHeader, CT_LABEL)
    h1:SetAnchor(LEFT, colHeader, LEFT, 8, 0)
    h1:SetFont("ZoFontGameBold")
    h1:SetText(ColorText("MEMBER", "FF9900"))

    local h2 = wm:CreateControl("$(parent)_H2", colHeader, CT_LABEL)
    h2:SetAnchor(LEFT, colHeader, LEFT, 155, 0)
    h2:SetFont("ZoFontGameBold")
    h2:SetText(ColorText("RANK ↕", "00FFCC"))
    h2:SetMouseEnabled(true)
    h2:SetHandler("OnMouseDown", function()
        if self.auditSortBy == "rank" then self.auditSortAsc = not self.auditSortAsc else self.auditSortBy = "rank"; self.auditSortAsc = true end
        self:UpdateAuditorUI()
    end)

    local h3 = wm:CreateControl("$(parent)_H3", colHeader, CT_LABEL)
    h3:SetAnchor(LEFT, colHeader, LEFT, 245, 0)
    h3:SetFont("ZoFontGameBold")
    h3:SetText(ColorText("OFFLINE ↕", "FFD700"))
    h3:SetMouseEnabled(true)
    h3:SetHandler("OnMouseDown", function()
        if self.auditSortBy == "days" then self.auditSortAsc = not self.auditSortAsc else self.auditSortBy = "days"; self.auditSortAsc = false end
        self:UpdateAuditorUI()
    end)

    local h4 = wm:CreateControl("$(parent)_H4", colHeader, CT_LABEL)
    h4:SetAnchor(LEFT, colHeader, LEFT, 325, 0)
    h4:SetFont("ZoFontGameBold")
    h4:SetText(ColorText("SALES ↕", "59E08A"))
    h4:SetMouseEnabled(true)
    h4:SetHandler("OnMouseDown", function()
        if self.auditSortBy == "sales" then self.auditSortAsc = not self.auditSortAsc else self.auditSortBy = "sales"; self.auditSortAsc = false end
        self:UpdateAuditorUI()
    end)

    local h5 = wm:CreateControl("$(parent)_H5", colHeader, CT_LABEL)
    h5:SetAnchor(LEFT, colHeader, LEFT, 420, 0)
    h5:SetFont("ZoFontGameBold")
    h5:SetText(ColorText("DUES ↕", "59E08A"))
    h5:SetMouseEnabled(true)
    h5:SetHandler("OnMouseDown", function()
        if self.auditSortBy == "dues" then self.auditSortAsc = not self.auditSortAsc else self.auditSortBy = "dues"; self.auditSortAsc = true end
        self:UpdateAuditorUI()
    end)

    local h6 = wm:CreateControl("$(parent)_H6", colHeader, CT_LABEL)
    h6:SetAnchor(LEFT, colHeader, LEFT, 510, 0)
    h6:SetFont("ZoFontGameBold")
    h6:SetText(ColorText("STATUS", "00FFCC"))

    local h7 = wm:CreateControl("$(parent)_H7", colHeader, CT_LABEL)
    h7:SetAnchor(LEFT, colHeader, LEFT, 595, 0)
    h7:SetFont("ZoFontGameBold")
    h7:SetText(ColorText("NOTE", "FFFFFF"))

    local h8 = wm:CreateControl("$(parent)_H8", colHeader, CT_LABEL)
    h8:SetAnchor(RIGHT, colHeader, RIGHT, -20, 0)
    h8:SetFont("ZoFontGameBold")
    h8:SetText(ColorText("ACTION", "FF9900"))

    -- 4. Table Rows (9 Rows)
    self.auditRows = {}
    local rowY = headerY + 24

    for r = 1, ROWS_PER_PAGE do
        local row = wm:CreateControl("$(parent)_Row_" .. r, card, CT_BACKDROP)
        row:SetAnchor(TOPLEFT, card, TOPLEFT, 10, rowY + (r - 1) * 35)
        row:SetAnchor(TOPRIGHT, card, TOPRIGHT, -10, rowY + (r - 1) * 35)
        row:SetHeight(33)
        row:SetCenterColor(0.05, 0.05, 0.07, 0.70)
        row:SetEdgeColor(0.20, 0.18, 0.14, 0.40)
        row:SetEdgeTexture("", 8, 1, 0)

        local nameLbl = wm:CreateControl("$(parent)_Name", row, CT_LABEL)
        nameLbl:SetAnchor(LEFT, row, LEFT, 8, 0)
        nameLbl:SetFont("ZoFontGameMedium")
        nameLbl:SetText("@Member")
        row.nameLbl = nameLbl

        local rankLbl = wm:CreateControl("$(parent)_Rank", row, CT_LABEL)
        rankLbl:SetAnchor(LEFT, row, LEFT, 155, 0)
        rankLbl:SetFont("ZoFontGameSmall")
        rankLbl:SetText("Member")
        row.rankLbl = rankLbl

        local daysLbl = wm:CreateControl("$(parent)_Days", row, CT_LABEL)
        daysLbl:SetAnchor(LEFT, row, LEFT, 245, 0)
        daysLbl:SetFont("ZoFontGameBold")
        daysLbl:SetText("14d")
        row.daysLbl = daysLbl

        local salesLbl = wm:CreateControl("$(parent)_Sales", row, CT_LABEL)
        salesLbl:SetAnchor(LEFT, row, LEFT, 325, 0)
        salesLbl:SetFont("ZoFontGameSmall")
        salesLbl:SetText("0")
        row.salesLbl = salesLbl

        local duesLbl = wm:CreateControl("$(parent)_Dues", row, CT_LABEL)
        duesLbl:SetAnchor(LEFT, row, LEFT, 420, 0)
        duesLbl:SetFont("ZoFontGameSmall")
        duesLbl:SetText("0g")
        row.duesLbl = duesLbl

        local statusLbl = wm:CreateControl("$(parent)_Status", row, CT_LABEL)
        statusLbl:SetAnchor(LEFT, row, LEFT, 510, 0)
        statusLbl:SetFont("ZoFontGameSmall")
        statusLbl:SetText("")
        row.statusLbl = statusLbl

        local noteLbl = wm:CreateControl("$(parent)_Note", row, CT_LABEL)
        noteLbl:SetAnchor(LEFT, row, LEFT, 595, 0)
        noteLbl:SetAnchor(RIGHT, row, RIGHT, -96, 0)
        noteLbl:SetFont("ZoFontGameSmall")
        noteLbl:SetText("")
        row.noteLbl = noteLbl

        local mailBtn = wm:CreateControl("$(parent)_MailBtn", row, CT_BUTTON)
        mailBtn:SetAnchor(RIGHT, row, RIGHT, -8, 0)
        mailBtn:SetDimensions(85, 22)
        mailBtn:SetFont("ZoFontGameSmall")
        mailBtn:SetText("Warn Mail")
        self:StyleTactileButton(mailBtn, {
            normalBg = { 0.12, 0.10, 0.04, 0.90 },
            hoverBg = { 0.18, 0.14, 0.06, 0.98 },
            normalEdge = { 0.75, 0.55, 0.10, 0.80 },
            hoverEdge = { 1.00, 0.85, 0.20, 1.00 },
            normalTextColor = { 1, 0.85, 0.2, 1 },
            hoverTextColor = { 1, 0.95, 0.5, 1 },
            tooltipTitle = "Stage Inactivity Warning Mail",
            tooltipText = "Open mail compose window addressed to this member with a polite check-in template (zero gold attached).",
        })
        row.mailBtn = mailBtn

        self.auditRows[r] = row
    end

    -- 5. Bottom Navigation & Status Bar
    local footerY = -8
    local statLbl = wm:CreateControl("$(parent)_StatLbl", card, CT_LABEL)
    statLbl:SetAnchor(BOTTOMLEFT, card, BOTTOMLEFT, 14, footerY)
    statLbl:SetFont("ZoFontGameSmall")
    statLbl:SetText("Roster: -- | Inactive: -- | Shielded: 0")
    self.auditStatLbl = statLbl

    -- Page controls
    local nextBtn = wm:CreateControl("$(parent)_NextBtn", card, CT_BUTTON)
    nextBtn:SetAnchor(BOTTOMRIGHT, card, BOTTOMRIGHT, -12, footerY + 2)
    nextBtn:SetDimensions(65, 22)
    nextBtn:SetFont("ZoFontGameSmall")
    nextBtn:SetText("Next >")
    self:StyleTactileButton(nextBtn, {
        normalBg = { 0.06, 0.06, 0.09, 0.85 },
        hoverBg = { 0.08, 0.16, 0.18, 0.95 },
        normalEdge = { 0.25, 0.25, 0.30, 0.60 },
        hoverEdge = { 0, 0.90, 0.80, 1.0 },
        normalTextColor = { 0.7, 0.7, 0.7, 1 },
        hoverTextColor = { 0, 1, 0.9, 1 },
        tooltipTitle = "Next Page",
        tooltipText = "View next page of flagged inactive members.",
    })
    nextBtn:SetHandler("OnClicked", function()
        local maxPages = math.max(1, math.ceil(#(self.auditFilteredMembers or {}) / ROWS_PER_PAGE))
        if self.auditCurrentPage < maxPages then
            self.auditCurrentPage = self.auditCurrentPage + 1
            self:RenderAuditorRows()
        end
    end)
    self.auditNextBtn = nextBtn

    local pageLbl = wm:CreateControl("$(parent)_PageLbl", card, CT_LABEL)
    pageLbl:SetAnchor(RIGHT, nextBtn, LEFT, -8, 0)
    pageLbl:SetFont("ZoFontGameSmall")
    pageLbl:SetText("Page 1/1")
    self.auditPageLbl = pageLbl

    local prevBtn = wm:CreateControl("$(parent)_PrevBtn", card, CT_BUTTON)
    prevBtn:SetAnchor(RIGHT, pageLbl, LEFT, -8, 0)
    prevBtn:SetDimensions(65, 22)
    prevBtn:SetFont("ZoFontGameSmall")
    prevBtn:SetText("< Prev")
    self:StyleTactileButton(prevBtn, {
        normalBg = { 0.06, 0.06, 0.09, 0.85 },
        hoverBg = { 0.08, 0.16, 0.18, 0.95 },
        normalEdge = { 0.25, 0.25, 0.30, 0.60 },
        hoverEdge = { 0, 0.90, 0.80, 1.0 },
        normalTextColor = { 0.7, 0.7, 0.7, 1 },
        hoverTextColor = { 0, 1, 0.9, 1 },
        tooltipTitle = "Previous Page",
        tooltipText = "View previous page of flagged inactive members.",
    })
    prevBtn:SetHandler("OnClicked", function()
        if self.auditCurrentPage > 1 then
            self.auditCurrentPage = self.auditCurrentPage - 1
            self:RenderAuditorRows()
        end
    end)
    self.auditPrevBtn = prevBtn

    local exportBtn = wm:CreateControl("$(parent)_ExportBtn", card, CT_BUTTON)
    exportBtn:SetAnchor(RIGHT, prevBtn, LEFT, -20, 0)
    exportBtn:SetDimensions(110, 22)
    exportBtn:SetFont("ZoFontGameSmall")
    exportBtn:SetText("Export to Chat")
    self:StyleTactileButton(exportBtn, {
        normalBg = { 0.12, 0.10, 0.04, 0.90 },
        hoverBg = { 0.18, 0.14, 0.06, 0.98 },
        normalEdge = { 0.75, 0.55, 0.10, 0.80 },
        hoverEdge = { 1.00, 0.85, 0.20, 1.00 },
        normalTextColor = { 1, 0.85, 0.2, 1 },
        hoverTextColor = { 1, 0.95, 0.5, 1 },
        tooltipTitle = "Export Inactivity Audit",
        tooltipText = "Print a clean summary of inactive members, sales, and bank dues to chat and SavedVariables for Discord export.",
    })
    exportBtn:SetHandler("OnClicked", function()
        self:ExportAuditToChat()
    end)
end

function FR:CycleAuditRankFilter()
    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)
    local numRanks = GetNumGuildRanks(guildId)

    if self.auditRankFilter == "all" then
        self.auditRankFilter = numRanks
    else
        local cur = tonumber(self.auditRankFilter) or numRanks
        cur = cur - 1
        if cur < 1 then
            self.auditRankFilter = "all"
        else
            self.auditRankFilter = cur
        end
    end

    self.auditCurrentPage = 1
    self:UpdateAuditorUI()
end

--[[ =========================================================================
     AUDITOR RENDERING & ACTIONS
========================================================================= ]]--

function FR:UpdateAuditorUI()
    self:RunRosterAudit()

    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)

    -- Update filter button visual states
    for d, btn in pairs(self.auditDayBtns or {}) do
        if btn.bg then
            if d == self.auditFilterDays then
                btn.isCustomActive = true
                btn.bg:SetCenterColor(0.18, 0.12, 0.04, 0.95)
                btn.bg:SetEdgeColor(0.95, 0.70, 0.15, 1.0)
                btn:SetNormalFontColor(1, 0.85, 0.2, 1)
            else
                btn.isCustomActive = false
                btn.bg:SetCenterColor(0.06, 0.06, 0.08, 0.80)
                btn.bg:SetEdgeColor(0.35, 0.28, 0.18, 0.60)
                btn:SetNormalFontColor(0.6, 0.6, 0.6, 1)
            end
        end
    end

    if self.auditOffToggle and self.auditOffToggle.bg then
        if self.auditExcludeOfficers then
            self.auditOffToggle.isCustomActive = true
            self.auditOffToggle.bg:SetCenterColor(0.04, 0.14, 0.08, 0.95)
            self.auditOffToggle.bg:SetEdgeColor(0.20, 0.85, 0.40, 0.85)
            self.auditOffToggle:SetNormalFontColor(0.3, 1, 0.5, 1)
        else
            self.auditOffToggle.isCustomActive = false
            self.auditOffToggle.bg:SetCenterColor(0.06, 0.06, 0.08, 0.80)
            self.auditOffToggle.bg:SetEdgeColor(0.35, 0.35, 0.35, 0.60)
            self.auditOffToggle:SetNormalFontColor(0.6, 0.6, 0.6, 1)
        end
    end

    if self.auditLoaToggle and self.auditLoaToggle.bg then
        if self.auditExcludeLOA then
            self.auditLoaToggle.isCustomActive = true
            self.auditLoaToggle.bg:SetCenterColor(0.04, 0.14, 0.08, 0.95)
            self.auditLoaToggle.bg:SetEdgeColor(0.20, 0.85, 0.40, 0.85)
            self.auditLoaToggle:SetNormalFontColor(0.3, 1, 0.5, 1)
        else
            self.auditLoaToggle.isCustomActive = false
            self.auditLoaToggle.bg:SetCenterColor(0.06, 0.06, 0.08, 0.80)
            self.auditLoaToggle.bg:SetEdgeColor(0.35, 0.35, 0.35, 0.60)
            self.auditLoaToggle:SetNormalFontColor(0.6, 0.6, 0.6, 1)
        end
    end

    if self.auditShieldToggle and self.auditShieldToggle.bg then
        if self.auditShieldActiveSellers then
            self.auditShieldToggle.isCustomActive = true
            self.auditShieldToggle.bg:SetCenterColor(0.04, 0.14, 0.18, 0.95)
            self.auditShieldToggle.bg:SetEdgeColor(0.0, 0.85, 0.95, 0.85)
            self.auditShieldToggle:SetNormalFontColor(0, 1, 0.9, 1)
        else
            self.auditShieldToggle.isCustomActive = false
            self.auditShieldToggle.bg:SetCenterColor(0.06, 0.06, 0.08, 0.80)
            self.auditShieldToggle.bg:SetEdgeColor(0.35, 0.35, 0.35, 0.60)
            self.auditShieldToggle:SetNormalFontColor(0.6, 0.6, 0.6, 1)
        end
    end

    if self.auditRankFilterBtn then
        if self.auditRankFilter == "all" then
            self.auditRankFilterBtn:SetText("Rank: All")
            if self.auditRankFilterBtn.bg then
                self.auditRankFilterBtn.bg:SetCenterColor(0.10, 0.08, 0.14, 0.85)
                self.auditRankFilterBtn.bg:SetEdgeColor(0.40, 0.30, 0.55, 0.60)
            end
        else
            local rName = GetGuildRankCustomName(guildId, tonumber(self.auditRankFilter) or 1)
            if not rName or rName == "" then rName = string.format("Rank %s", tostring(self.auditRankFilter)) end
            if #rName > 10 then rName = rName:sub(1, 8) .. ".." end
            self.auditRankFilterBtn:SetText("Rank: " .. rName)
            if self.auditRankFilterBtn.bg then
                self.auditRankFilterBtn.bg:SetCenterColor(0.18, 0.10, 0.25, 0.95)
                self.auditRankFilterBtn.bg:SetEdgeColor(0.85, 0.50, 1.00, 1.00)
            end
        end
    end

    self:RenderAuditorRows()
end

function FR:RenderAuditorRows()
    local members = self.auditFilteredMembers or {}
    local total = #members
    local maxPages = math.max(1, math.ceil(total / ROWS_PER_PAGE))
    if self.auditCurrentPage > maxPages then self.auditCurrentPage = maxPages end
    if self.auditCurrentPage < 1 then self.auditCurrentPage = 1 end

    local startIndex = (self.auditCurrentPage - 1) * ROWS_PER_PAGE

    for r = 1, ROWS_PER_PAGE do
        local row = self.auditRows and self.auditRows[r]
        local memberIndex = startIndex + r

        if row then
            if memberIndex <= total then
                local m = members[memberIndex]
                row:SetHidden(false)

                -- Name
                row.nameLbl:SetText(m.name)
                row.nameLbl:SetMouseEnabled(true)
                row.nameLbl:SetHandler("OnMouseEnter", function(ctrl)
                    InitializeTooltip(InformationTooltip, ctrl, TOP, 0, -4)
                    local duesColor = m.duesMet and "59E08A" or "FF5555"
                    SetTooltipText(InformationTooltip, string.format(
                        "|c00FFCC%s|r\n|c888888Rank: %s\nOffline: %d days|r\n|c59E08ASales Recorded: %d (%sg)|r\n|cFFD700Bank Deposits: %sg|r\n|c%sDues Status: %s (%s)|r",
                        m.name, m.rank, m.days, m.salesCount or 0, ZO_LocalizeDecimalNumber(m.salesGold or 0),
                        ZO_LocalizeDecimalNumber(m.deposits or 0), duesColor, m.duesMet and "DUES MET" or "MISSING DUES", m.duesReason or "Unmet"))
                end)
                row.nameLbl:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)

                -- Rank
                row.rankLbl:SetText(m.rank)

                -- Offline duration
                local dayColor = m.days >= 30 and "FF5555" or (m.days >= 14 and "FFAA00" or "FFD700")
                row.daysLbl:SetText(string.format("|c%s%d days|r", dayColor, m.days))

                -- Sales
                if m.salesCount and m.salesCount > 0 then
                    local goldK = math.floor((m.salesGold or 0) / 1000)
                    row.salesLbl:SetText(string.format("|c59E08A%d|r |c888888(%dk)|r", m.salesCount, goldK))
                else
                    row.salesLbl:SetText("|c6666660|r")
                end

                -- Bank Dues
                local depText = m.deposits > 0 and string.format("|c59E08A%sg|r", ZO_LocalizeDecimalNumber(m.deposits)) or "|c6666660g|r"
                row.duesLbl:SetText(depText)

                -- Dues Status Badge
                if m.duesMet then
                    row.statusLbl:SetText("|c59E08A[MET ✓]|r")
                else
                    row.statusLbl:SetText("|cFF5555[UNMET]|r")
                end

                -- Note
                local cleanNote = string.gsub(m.note or "", "\n", " ")
                if #cleanNote > 12 then
                    cleanNote = string.sub(cleanNote, 1, 10) .. ".."
                end
                row.noteLbl:SetText(cleanNote)
                row.noteLbl:SetMouseEnabled(true)
                row.noteLbl:SetHandler("OnMouseEnter", function(ctrl)
                    if m.note and m.note ~= "" then
                        InitializeTooltip(InformationTooltip, ctrl, TOP, 0, -4)
                        SetTooltipText(InformationTooltip, string.format("|cFF9900%s Member Note:|r\n|cFFFFFF%s|r", m.name, m.note))
                    end
                end)
                row.noteLbl:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)

                -- Warn Mail button handler
                row.mailBtn:SetHandler("OnClicked", function()
                    self:TriggerInactivityMailHandoff(m.name, m.days)
                end)
            else
                row:SetHidden(true)
            end
        end
    end

    if self.auditPageLbl then
        self.auditPageLbl:SetText(string.format("Page %d/%d (%d shown)", self.auditCurrentPage, maxPages, total))
    end

    if self.auditStatLbl then
        self.auditStatLbl:SetText(string.format("Roster: %d | Inactive: |cFF5555%d|r | Shielded Active: |c59E08A%d|r | Excused: %d",
            self.auditTotalMembers or 0, total, self.auditShieldedCount or 0, self.auditExcusedCount or 0))
    end
end

function FR:TriggerInactivityMailHandoff(memberName, daysOffline)
    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)
    local guildName = GetGuildName(guildId)

    if self.StageInactivityWarning then
        self:StageInactivityWarning(memberName, guildName, daysOffline)
    else
        -- Fallback: open mail send and fill fields directly
        SCENE_MANAGER:Show("mailSend")
        zo_callLater(function()
            ZO_MailSendToField:SetText(memberName)
            ZO_MailSendSubjectField:SetText(string.format("[%s] Roster Check-in", guildName))
            ZO_MailSendBodyField:SetText(string.format("Greetings %s,\n\nThis is a friendly check-in from %s! We noticed you haven't logged in for %d days. If you are taking a break, please let an officer know so we can safeguard your roster spot!\n\nWarm regards,\n%s Staff",
                memberName, guildName, daysOffline, guildName))
            ZO_MailSendBodyField:TakeFocus()
        end, 200)
    end

    self.PrintChat(string.format("Inactivity warning staged for %s (%d days offline in %s).",
        memberName, daysOffline, guildName))
end

function FR:ExportAuditToChat()
    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)
    local guildName = GetGuildName(guildId)
    local members = self.auditFilteredMembers or {}

    self.PrintChat(string.format("=== Inactivity Purge Audit: %s (%d+ Days Offline) ===", guildName, self.auditFilterDays or 14))
    self.PrintChat(string.format("Found %d flagged members. Summary:", #members))

    local count = math.min(#members, 15)
    for i = 1, count do
        local m = members[i]
        df("  - %s (%d days offline, %s) | Sales: %d (%sg) | Bank: %sg | %s",
            m.name, m.days, m.rank, m.salesCount or 0, ZO_LocalizeDecimalNumber(m.salesGold or 0),
            ZO_LocalizeDecimalNumber(m.deposits or 0), m.duesMet and "|c59E08A[DUES MET]|r" or "|cFF5555[UNMET]|r")
    end
    if #members > count then
        df("  ...and %d more inactive members saved to SavedVariables.", #members - count)
    end
end

--[[ =========================================================================
     CHUNKED AUDIT REBUILD & SHADOW RECONCILIATION ENGINE
     (Rulings 4.5 & 4.7 - Claude Fable 5.1)
========================================================================= ]]--

local FRAME_BUDGET_MS = 2.0
local MAX_PER_TICK    = 2000

function FR:BeginRebuild(domain)
    if not self.savedVars or not self.savedVars.audit or not self.savedVars.audit.domains then return end
    local a = self.savedVars.audit.domains[domain]
    if not a or a.scan then return end -- already running

    local records = (domain == "sales") and self.savedVars.sales or (self.savedVars.staff and self.savedVars.staff.bankDeposits)
    if not records then return end

    a.scan = {
        ceiling  = self.savedVars.nextSeq or 0,
        count    = 0,
        iter     = nil,
        pending  = {},
        startGen = a.generation or 0,
    }
    a.valid = false

    local updateEventName = "FissalAuditRebuild_" .. domain
    EVENT_MANAGER:RegisterForUpdate(updateEventName, 0, function()
        self:RebuildTick(domain)
    end)
end

function FR:RebuildTick(domain)
    local a = self.savedVars.audit.domains[domain]
    if not a or not a.scan then return end
    local s = a.scan
    local records = (domain == "sales") and self.savedVars.sales or (self.savedVars.staff and self.savedVars.staff.bankDeposits)
    if not records then
        EVENT_MANAGER:UnregisterForUpdate("FissalAuditRebuild_" .. domain)
        a.scan = nil
        return
    end

    local t0 = GetGameTimeMilliseconds()
    local n = 0
    local k, v = next(records, s.iter)

    while k ~= nil do
        local seq = v.seq or 0
        if seq <= s.ceiling then
            s.count = s.count + 1
        end
        n = n + 1
        if n >= MAX_PER_TICK or (GetGameTimeMilliseconds() - t0) >= FRAME_BUDGET_MS then
            s.iter = k
            return
        end
        k, v = next(records, k)
    end

    -- Scan complete
    EVENT_MANAGER:UnregisterForUpdate("FissalAuditRebuild_" .. domain)

    if (a.generation or 0) ~= s.startGen then
        -- Something pruned/mutated mid-scan; restart cleanly
        a.scan = nil
        return self:BeginRebuild(domain)
    end

    self:CommitRebuild(domain)
end

function FR:CommitRebuild(domain)
    local a = self.savedVars.audit.domains[domain]
    if not a or not a.scan then return end
    local s = a.scan

    -- Step 1: adopt scan result. Everything <= ceiling is now counted.
    a.counter   = s.count
    a.watermark = s.ceiling

    -- Step 2: drain buffered live events strictly > ceiling
    table.sort(s.pending)
    for _, seq in ipairs(s.pending) do
        if seq > a.watermark then
            a.watermark = seq
            a.counter   = a.counter + 1
        end
    end

    a.scan  = nil
    a.valid = true

    if self.UpdateHUD then self:UpdateHUD() end
    if self.UpdateConsoleStatus then self:UpdateConsoleStatus() end
end

-- Slow count strictly for shadow reconciliation (never used in UI or frame loops)
function FR:_SlowCount(tbl)
    if not tbl then return 0 end
    local count = 0
    for _ in pairs(tbl) do
        count = count + 1
    end
    return count
end

function FR:ScheduleShadowReconciliation()
    local function TryReconcile()
        if IsUnitInCombat and IsUnitInCombat("player") then
            zo_callLater(TryReconcile, 15000)
            return
        end
        self:RunShadowReconciliation()
    end
    zo_callLater(TryReconcile, 10000)
end

function FR:RunShadowReconciliation()
    if not self.savedVars or not self.savedVars.audit or not self.savedVars.audit.domains then return end
    local audit = self.savedVars.audit

    -- Shadow check sales
    local liveSales = audit.domains.sales and audit.domains.sales.counter or 0
    local shadowSales = self:_SlowCount(self.savedVars.sales)
    if liveSales ~= shadowSales then
        audit.domains.sales.counter = shadowSales
        audit.domains.sales.watermark = self.savedVars.nextSeq or 0
        audit.domains.sales.valid = true
        audit.driftEvents = (audit.driftEvents or 0) + 1
    end

    -- Shadow check deposits
    local liveDeposits = audit.domains.deposits and audit.domains.deposits.counter or 0
    local shadowDeposits = self:_SlowCount(self.savedVars.staff and self.savedVars.staff.bankDeposits)
    if liveDeposits ~= shadowDeposits then
        audit.domains.deposits.counter = shadowDeposits
        audit.domains.deposits.watermark = self.savedVars.nextSeq or 0
        audit.domains.deposits.valid = true
        audit.driftEvents = (audit.driftEvents or 0) + 1
    end
end
