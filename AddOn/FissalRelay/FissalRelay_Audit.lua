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

local AUDIT_ACTIONS = { "Warn Mail", "Kick & Mail", "Kick Only", "Exempt [LOA]", "Void / Hide" }

local AUDIT_ACTION_THEMES = {
    ["Warn Mail"] = {
        normalBg = { 0.14, 0.10, 0.04, 0.90 },
        hoverBg = { 0.22, 0.16, 0.06, 0.98 },
        normalEdge = { 0.85, 0.65, 0.15, 0.90 },
        hoverEdge = { 1.00, 0.85, 0.25, 1.00 },
        normalTextColor = { 1, 0.85, 0.2, 1 },
        hoverTextColor = { 1, 0.95, 0.5, 1 },
        tipTitle = "Action: Warn Mail",
        tipText = "Stage a friendly inactivity notice mail to check in before taking roster action.",
    },
    ["Kick & Mail"] = {
        normalBg = { 0.16, 0.08, 0.04, 0.90 },
        hoverBg = { 0.24, 0.12, 0.06, 0.98 },
        normalEdge = { 0.95, 0.45, 0.15, 0.95 },
        hoverEdge = { 1.00, 0.60, 0.20, 1.00 },
        normalTextColor = { 1, 0.55, 0.1, 1 },
        hoverTextColor = { 1, 0.75, 0.3, 1 },
        tipTitle = "Action: Kick & Mail",
        tipText = "Send courtesy removal notification with invite-back link, then remove from roster.",
    },
    ["Kick Only"] = {
        normalBg = { 0.20, 0.05, 0.05, 0.95 },
        hoverBg = { 0.30, 0.08, 0.08, 1.00 },
        normalEdge = { 0.95, 0.20, 0.20, 1.00 },
        hoverEdge = { 1.00, 0.35, 0.35, 1.00 },
        normalTextColor = { 1, 0.25, 0.25, 1 },
        hoverTextColor = { 1, 0.50, 0.50, 1 },
        tipTitle = "Action: Kick Only",
        tipText = "Immediately remove from guild roster without sending in-game mail.",
    },
    ["Exempt [LOA]"] = {
        normalBg = { 0.04, 0.14, 0.12, 0.90 },
        hoverBg = { 0.06, 0.22, 0.18, 0.98 },
        normalEdge = { 0.00, 0.85, 0.75, 0.90 },
        hoverEdge = { 0.20, 1.00, 0.90, 1.00 },
        normalTextColor = { 0, 1, 0.8, 1 },
        hoverTextColor = { 0.4, 1, 0.9, 1 },
        tipTitle = "Action: Exempt [LOA]",
        tipText = "Mark member with Leave of Absence tag in guild note to prevent future audit flagging.",
    },
    ["Void / Hide"] = {
        normalBg = { 0.12, 0.06, 0.18, 0.90 },
        hoverBg = { 0.18, 0.10, 0.26, 0.98 },
        normalEdge = { 0.70, 0.40, 0.95, 0.90 },
        hoverEdge = { 0.85, 0.55, 1.00, 1.00 },
        normalTextColor = { 0.7, 0.5, 1, 1 },
        hoverTextColor = { 0.85, 0.7, 1, 1 },
        tipTitle = "Action: Void / Hide",
        tipText = "Permanently exclude this member (@Account) from audit lists across all sessions.",
    },
}

local function ApplyAuditActionStyle(btn, actionName)
    if not btn then return end
    local theme = AUDIT_ACTION_THEMES[actionName] or AUDIT_ACTION_THEMES["Warn Mail"]
    FR:UpdateTactileTheme(btn, theme)
    btn:SetText(actionName .. " ▾")
end

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

    local myDisplayName = string.lower(string.gsub(GetDisplayName() or "", "^@", ""))
    local myMemberIdx = GetGuildMemberIndexFromDisplayName and GetGuildMemberIndexFromDisplayName(guildId, GetDisplayName())
    local myRankIndex = 99
    if myMemberIdx and myMemberIdx > 0 then
        local _, _, mRank = GetGuildMemberInfo(guildId, myMemberIdx)
        if mRank then myRankIndex = mRank end
    else
        self.PrintChat(string.format("|cFF9900[Safety Notice]|r Could not resolve rank for %s in %s; kick candidate listing disabled for safety.", myDisplayName, guildName))
    end

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

            local memberIsGM = (IsGuildRankGuildMaster and IsGuildRankGuildMaster(guildId, rankIndex)) or (rankIndex == 1)

            local passFilter = true
            -- Permanent Void / Exclusion Guard
            local exclusions = self.savedVars and self.savedVars.auditExclusions and self.savedVars.auditExclusions[guildId]
            if exclusions and (exclusions[lowerName] or exclusions["@" .. lowerName]) then
                passFilter = false
            end

            -- Unconditional Guard (Fable 5.1 B1): Never list GM, executing staff account, or members at/above executor rank for kick
            if lowerName == myDisplayName or memberIsGM or (rankIndex <= myRankIndex) then
                passFilter = false
            end
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

    local voidedCount = 0
    if self.savedVars and self.savedVars.auditExclusions and self.savedVars.auditExclusions[guildId] then
        for _ in pairs(self.savedVars.auditExclusions[guildId]) do
            voidedCount = voidedCount + 1
        end
    end
    self.auditVoidedCount = voidedCount
    if self.auditExclusionsBtn then
        self.auditExclusionsBtn:SetText(string.format("Void (%d)", voidedCount))
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
        btn:SetAnchor(TOPLEFT, card, TOPLEFT, 85 + (idx - 1) * 42, 7)
        btn:SetDimensions(38, 22)
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
    offToggle:SetAnchor(TOPLEFT, card, TOPLEFT, 182, 7)
    offToggle:SetDimensions(82, 22)
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
    loaToggle:SetAnchor(TOPLEFT, card, TOPLEFT, 270, 7)
    loaToggle:SetDimensions(75, 22)
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
    shieldToggle:SetAnchor(TOPLEFT, card, TOPLEFT, 351, 7)
    shieldToggle:SetDimensions(95, 22)
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

    self.auditFilterControls = {
        filterLbl,
        offToggle,
        loaToggle,
        shieldToggle,
        rankBtn,
        exclusionsBtn,
        batchBtn,
        searchBg,
    }
    for _, dBtn in pairs(self.auditDayBtns or {}) do
        table.insert(self.auditFilterControls, dBtn)
    end

    -- Rank Filter Cycle Button
    -- Search Box Container (anchored top right)
    local searchBg = wm:CreateControlFromVirtual("$(parent)_SearchBg", card, "ZO_EditBackdrop")
    searchBg:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 6)
    searchBg:SetDimensions(115, 24)

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

    -- Batch Auto-Processor Button (anchored left of search box)
    local batchBtn = wm:CreateControl("$(parent)_BatchBtn", card, CT_BUTTON)
    batchBtn:SetAnchor(RIGHT, searchBg, LEFT, -6, 0)
    batchBtn:SetDimensions(80, 22)
    batchBtn:SetFont("ZoFontGameBold")
    batchBtn:SetText("Batch Run")
    self:StyleTactileButton(batchBtn, {
        normalBg = { 0.18, 0.10, 0.04, 0.90 },
        hoverBg = { 0.26, 0.15, 0.06, 0.98 },
        normalEdge = { 0.90, 0.55, 0.15, 0.90 },
        hoverEdge = { 1.00, 0.75, 0.20, 1.00 },
        normalTextColor = { 1, 0.85, 0.20, 1 },
        hoverTextColor = { 1, 0.95, 0.50, 1 },
        tooltipTitle = "Batch Auto-Process",
        tooltipText = "Sequentially execute all selected/staged actions with safe 1.5s pacing and live progress.",
    })
    batchBtn:SetHandler("OnClicked", function()
        self:StartAuditBatch()
    end)
    self.auditBatchBtn = batchBtn

    -- Permanent Void / Exclusions Manager Button (anchored left of batch button)
    local exclusionsBtn = wm:CreateControl("$(parent)_ExclusionsBtn", card, CT_BUTTON)
    exclusionsBtn:SetAnchor(RIGHT, batchBtn, LEFT, -6, 0)
    exclusionsBtn:SetDimensions(75, 22)
    exclusionsBtn:SetFont("ZoFontGameSmall")
    exclusionsBtn:SetText("Void (0)")
    self:StyleTactileButton(exclusionsBtn, {
        normalBg = { 0.10, 0.06, 0.16, 0.85 },
        hoverBg = { 0.16, 0.10, 0.24, 0.95 },
        normalEdge = { 0.60, 0.35, 0.85, 0.80 },
        hoverEdge = { 0.80, 0.50, 1.00, 1.00 },
        normalTextColor = { 0.75, 0.55, 1, 1 },
        hoverTextColor = { 0.90, 0.75, 1, 1 },
        tooltipTitle = "Permanent Void Registry",
        tooltipText = "View and unhide members permanently excluded from inactivity auditing.",
    })
    exclusionsBtn:SetHandler("OnClicked", function()
        self:ToggleAuditExclusionsDrawer()
    end)
    self.auditExclusionsBtn = exclusionsBtn

    -- Rank Filter Cycle Button (anchored left of void button)
    local rankBtn = wm:CreateControl("$(parent)_RankFilterBtn", card, CT_BUTTON)
    rankBtn:SetAnchor(RIGHT, exclusionsBtn, LEFT, -6, 0)
    rankBtn:SetDimensions(95, 22)
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

    -- 3. Table Column Headers
    local headerY = 36
    local colHeader = wm:CreateControl("$(parent)_Header", card, CT_BACKDROP)
    colHeader:SetAnchor(TOPLEFT, card, TOPLEFT, 10, headerY)
    colHeader:SetAnchor(TOPRIGHT, card, TOPRIGHT, -10, headerY)
    colHeader:SetHeight(22)
    colHeader:SetCenterColor(0.10, 0.10, 0.14, 0.90)
    colHeader:SetEdgeColor(0.25, 0.20, 0.15, 0.60)
    colHeader:SetEdgeTexture("", 8, 1, 0)
    self.auditColHeader = colHeader

    local h1 = wm:CreateControl("$(parent)_H1", colHeader, CT_LABEL)
    h1:SetAnchor(LEFT, colHeader, LEFT, 10, 0)
    h1:SetFont("ZoFontGameBold")
    h1:SetText(ColorText("MEMBER", "FF9900"))

    local h2 = wm:CreateControl("$(parent)_H2", colHeader, CT_LABEL)
    h2:SetAnchor(LEFT, colHeader, LEFT, 190, 0)
    h2:SetFont("ZoFontGameBold")
    h2:SetText(ColorText("RANK", "00FFCC"))
    h2:SetMouseEnabled(true)
    h2:SetHandler("OnMouseDown", function()
        if self.auditSortBy == "rank" then self.auditSortAsc = not self.auditSortAsc else self.auditSortBy = "rank"; self.auditSortAsc = true end
        self:UpdateAuditorUI()
    end)
    self.auditHdrRank = h2

    local h3 = wm:CreateControl("$(parent)_H3", colHeader, CT_LABEL)
    h3:SetAnchor(LEFT, colHeader, LEFT, 300, 0)
    h3:SetFont("ZoFontGameBold")
    h3:SetText(ColorText("OFFLINE", "FFD700"))
    h3:SetMouseEnabled(true)
    h3:SetHandler("OnMouseDown", function()
        if self.auditSortBy == "days" then self.auditSortAsc = not self.auditSortAsc else self.auditSortBy = "days"; self.auditSortAsc = false end
        self:UpdateAuditorUI()
    end)
    self.auditHdrOffline = h3

    local h4 = wm:CreateControl("$(parent)_H4", colHeader, CT_LABEL)
    h4:SetAnchor(LEFT, colHeader, LEFT, 370, 0)
    h4:SetFont("ZoFontGameBold")
    h4:SetText(ColorText("SALES", "59E08A"))
    h4:SetMouseEnabled(true)
    h4:SetHandler("OnMouseDown", function()
        if self.auditSortBy == "sales" then self.auditSortAsc = not self.auditSortAsc else self.auditSortBy = "sales"; self.auditSortAsc = false end
        self:UpdateAuditorUI()
    end)
    self.auditHdrSales = h4

    local h5 = wm:CreateControl("$(parent)_H5", colHeader, CT_LABEL)
    h5:SetAnchor(LEFT, colHeader, LEFT, 450, 0)
    h5:SetFont("ZoFontGameBold")
    h5:SetText(ColorText("DUES", "59E08A"))
    h5:SetMouseEnabled(true)
    h5:SetHandler("OnMouseDown", function()
        if self.auditSortBy == "dues" then self.auditSortAsc = not self.auditSortAsc else self.auditSortBy = "dues"; self.auditSortAsc = false end
        self:UpdateAuditorUI()
    end)
    self.auditHdrDues = h5

    local h6 = wm:CreateControl("$(parent)_H6", colHeader, CT_LABEL)
    h6:SetAnchor(LEFT, colHeader, LEFT, 525, 0)
    h6:SetFont("ZoFontGameBold")
    h6:SetText(ColorText("NOTE / MAIL", "00FFCC"))

    local h8 = wm:CreateControl("$(parent)_H8", colHeader, CT_LABEL)
    h8:SetAnchor(LEFT, colHeader, LEFT, 640, 0)
    h8:SetFont("ZoFontGameBold")
    h8:SetText(ColorText("ACTION / DISPATCH", "FF9900"))

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
        nameLbl:SetAnchor(LEFT, row, LEFT, 10, 0)
        nameLbl:SetDimensions(175, 22)
        nameLbl:SetFont("ZoFontGameMedium")
        nameLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        nameLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        nameLbl:SetText("@Member")
        row.nameLbl = nameLbl

        local rankLbl = wm:CreateControl("$(parent)_Rank", row, CT_LABEL)
        rankLbl:SetAnchor(LEFT, row, LEFT, 190, 0)
        rankLbl:SetDimensions(105, 22)
        rankLbl:SetFont("ZoFontGameSmall")
        rankLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        rankLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        rankLbl:SetText("Member")
        row.rankLbl = rankLbl

        local daysLbl = wm:CreateControl("$(parent)_Days", row, CT_LABEL)
        daysLbl:SetAnchor(LEFT, row, LEFT, 300, 0)
        daysLbl:SetDimensions(65, 22)
        daysLbl:SetFont("ZoFontGameBold")
        daysLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        daysLbl:SetText("14d")
        row.daysLbl = daysLbl

        local salesLbl = wm:CreateControl("$(parent)_Sales", row, CT_LABEL)
        salesLbl:SetAnchor(LEFT, row, LEFT, 370, 0)
        salesLbl:SetDimensions(75, 22)
        salesLbl:SetFont("ZoFontGameSmall")
        salesLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        salesLbl:SetText("0g")
        row.salesLbl = salesLbl

        local duesLbl = wm:CreateControl("$(parent)_Dues", row, CT_LABEL)
        duesLbl:SetAnchor(LEFT, row, LEFT, 450, 0)
        duesLbl:SetDimensions(70, 22)
        duesLbl:SetFont("ZoFontGameSmall")
        duesLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        duesLbl:SetText("0g")
        row.duesLbl = duesLbl

        local noteLbl = wm:CreateControl("$(parent)_Note", row, CT_LABEL)
        noteLbl:SetAnchor(LEFT, row, LEFT, 525, 0)
        noteLbl:SetDimensions(110, 22)
        noteLbl:SetFont("ZoFontGameSmall")
        noteLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        noteLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        noteLbl:SetText("--")
        row.noteLbl = noteLbl

        -- Action Selector Button (Cycles actions across 5 color themes)
        local actionBtn = wm:CreateControl("$(parent)_ActionBtn", row, CT_BUTTON)
        actionBtn:SetAnchor(LEFT, row, LEFT, 640, 0)
        actionBtn:SetDimensions(125, 22)
        actionBtn:SetFont("ZoFontGameSmall")
        row.actionIndex = 1
        row.selectedAction = "Warn Mail"

        self:StyleTactileButton(actionBtn, AUDIT_ACTION_THEMES["Warn Mail"])
        ApplyAuditActionStyle(actionBtn, "Warn Mail")

        actionBtn:SetHandler("OnClicked", function()
            row.actionIndex = (row.actionIndex % #AUDIT_ACTIONS) + 1
            row.selectedAction = AUDIT_ACTIONS[row.actionIndex]
            ApplyAuditActionStyle(actionBtn, row.selectedAction)
            if row.memberData then
                row.memberData.stagedAction = row.selectedAction
            end
        end)
        row.actionBtn = actionBtn

        local applyBtn = wm:CreateControl("$(parent)_ApplyBtn", row, CT_BUTTON)
        applyBtn:SetAnchor(LEFT, actionBtn, RIGHT, 6, 0)
        applyBtn:SetDimensions(65, 22)
        applyBtn:SetFont("ZoFontGameBold")
        applyBtn:SetText("Apply")
        self:StyleTactileButton(applyBtn, {
            normalBg = { 0.04, 0.14, 0.10, 0.90 },
            hoverBg = { 0.06, 0.20, 0.14, 0.98 },
            normalEdge = { 0.20, 0.80, 0.40, 0.85 },
            hoverEdge = { 0.30, 1.00, 0.50, 1.00 },
            normalTextColor = { 0.3, 1, 0.5, 1 },
            hoverTextColor = { 0.6, 1, 0.7, 1 },
            tooltipTitle = "Apply Action",
            tooltipText = "Execute the selected action on this member with safe confirmation.",
        })
        row.applyBtn = applyBtn

        self.auditRows[r] = row
    end

    -- Mouse Wheel support on card for smooth list scrolling
    card:SetMouseEnabled(true)
    card:SetHandler("OnMouseWheel", function(control, delta)
        local maxPages = math.max(1, math.ceil(#(self.auditFilteredMembers or {}) / ROWS_PER_PAGE))
        if delta > 0 then
            if self.auditCurrentPage > 1 then
                self.auditCurrentPage = self.auditCurrentPage - 1
                self:RenderAuditorRows()
            end
        elseif delta < 0 then
            if self.auditCurrentPage < maxPages then
                self.auditCurrentPage = self.auditCurrentPage + 1
                self:RenderAuditorRows()
            end
        end
    end)

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
    self.auditExportBtn = exportBtn

    self:BuildAuditBatchProgressPanel(card)
    self:BuildAuditExclusionsDrawer(card)
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
    if self.auditExclusionsDrawer and not self.auditExclusionsDrawer:IsHidden() then
        return
    end

    self:RunRosterAudit()

    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)

    -- Dynamic ASCII sort indicators
    local rankSort = (self.auditSortBy == "rank") and (self.auditSortAsc and " ^" or " v") or ""
    local daysSort = (self.auditSortBy == "days") and (self.auditSortAsc and " ^" or " v") or ""
    local salesSort = (self.auditSortBy == "sales") and (self.auditSortAsc and " ^" or " v") or ""
    local duesSort = (self.auditSortBy == "dues") and (self.auditSortAsc and " ^" or " v") or ""
    if self.auditHdrRank then self.auditHdrRank:SetText(ColorText("RANK" .. rankSort, "00FFCC")) end
    if self.auditHdrOffline then self.auditHdrOffline:SetText(ColorText("OFFLINE" .. daysSort, "FFD700")) end
    if self.auditHdrSales then self.auditHdrSales:SetText(ColorText("SALES" .. salesSort, "59E08A")) end
    if self.auditHdrDues then self.auditHdrDues:SetText(ColorText("DUES" .. duesSort, "59E08A")) end

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
            if #rName > 8 then rName = rName:sub(1, 6) .. ".." end
            self.auditRankFilterBtn:SetText("Rank: " .. rName)
            if self.auditRankFilterBtn.bg then
                self.auditRankFilterBtn.bg:SetCenterColor(0.18, 0.10, 0.25, 0.95)
                self.auditRankFilterBtn.bg:SetEdgeColor(0.85, 0.50, 1.00, 1.00)
            end
        end
    end

    self:RenderAuditorRows()
end

function FR:RecordMemberMailSent(memberName, mailType)
    if not self.savedVars then return end
    if not self.savedVars.staff then self.savedVars.staff = {} end
    if not self.savedVars.staff.mailHistory then self.savedVars.staff.mailHistory = {} end
    local clean = string.gsub(string.lower(memberName or ""), "^@", "")
    local hist = self.savedVars.staff.mailHistory[clean] or { count = 0, lastMailTime = 0, types = {} }
    hist.count = (hist.count or 0) + 1
    hist.lastMailTime = GetTimeStamp()
    table.insert(hist.types, { type = mailType or "warn", time = hist.lastMailTime })
    self.savedVars.staff.mailHistory[clean] = hist
end

function FR:GetMemberMailInfo(memberName)
    if not self.savedVars or not self.savedVars.staff or not self.savedVars.staff.mailHistory then return 0, 0 end
    local clean = string.gsub(string.lower(memberName or ""), "^@", "")
    local hist = self.savedVars.staff.mailHistory[clean]
    if hist then
        return hist.count or 0, hist.lastMailTime or 0
    end
    return 0, 0
end

local function RegisterAuditorCustomDialogs()
    if ESO_Dialogs and not ESO_Dialogs["FISSAL_CONFIRM_KICK_AND_MAIL"] then
        ESO_Dialogs["FISSAL_CONFIRM_KICK_AND_MAIL"] = {
            title = { text = "Confirm Kick & Courtesy Mail" },
            mainText = { text = "Remove |c00FFCC<<1>>|r from |c00FFCC<<2>>|r and send courtesy re-invite mail?\n\n|cCCCCCCMail Message:|r\n|cFFFFFFThank you for being part of <<2>>! As our trading roster is currently full, we had to open up your space to keep trades flowing while you take a break. You are always warmly welcome back whenever you return to Tamriel—simply message an officer or re-apply!\n\nWarm regards,\n<<2>> Staff|r" },
            buttons = {
                {
                    text = SI_DIALOG_CONFIRM,
                    callback = function(dialog)
                        if dialog.data and dialog.data.onConfirm then dialog.data.onConfirm() end
                    end,
                },
                { text = SI_DIALOG_CANCEL },
            },
        }
    end

    if ESO_Dialogs and not ESO_Dialogs["FISSAL_CONFIRM_KICK_MEMBER"] then
        ESO_Dialogs["FISSAL_CONFIRM_KICK_MEMBER"] = {
            title = { text = "Confirm Guild Removal" },
            mainText = { text = "Remove |c00FFCC<<1>>|r from |c00FFCC<<2>>|r without sending mail?" },
            buttons = {
                {
                    text = SI_DIALOG_CONFIRM,
                    callback = function(dialog)
                        if dialog.data and dialog.data.onConfirm then dialog.data.onConfirm() end
                    end,
                },
                { text = SI_DIALOG_CANCEL },
            },
        }
    end

    if ESO_Dialogs and not ESO_Dialogs["FISSAL_CONFIRM_AUDIT_BATCH"] then
        ESO_Dialogs["FISSAL_CONFIRM_AUDIT_BATCH"] = {
            title = { text = "Confirm Batch Inactivity Processing" },
            mainText = { text = "<<1>>" },
            buttons = {
                {
                    text = SI_DIALOG_CONFIRM,
                    callback = function(dialog)
                        if dialog.data and dialog.data.onConfirm then dialog.data.onConfirm() end
                    end,
                },
                { text = SI_DIALOG_CANCEL },
            },
        }
    end
end

function FR:CheckAuditActionPermission(guildId, targetName, action)
    local playerDisplayName = GetDisplayName()
    if string.lower(targetName) == string.lower(playerDisplayName) then
        return false, "Cannot target yourself!"
    end

    local mIdx = GetGuildMemberIndexFromDisplayName and GetGuildMemberIndexFromDisplayName(guildId, targetName)
    if not mIdx or mIdx <= 0 then
        local numM = GetNumGuildMembers(guildId)
        for i = 1, numM do
            local dName = GetGuildMemberInfo(guildId, i)
            if string.lower(dName) == string.lower(targetName) then
                mIdx = i
                break
            end
        end
    end

    if action == "Kick Only" or action == "Kick & Mail" then
        if not DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_REMOVE) then
            return false, "Missing guild permission: Remove Member."
        end

        local myIdx = GetGuildMemberIndexFromDisplayName and GetGuildMemberIndexFromDisplayName(guildId, playerDisplayName)
        local myRank = myIdx and select(3, GetGuildMemberInfo(guildId, myIdx))
        local targetRank = mIdx and select(3, GetGuildMemberInfo(guildId, mIdx))
        if myRank and targetRank then
            -- Lower rank number = higher rank (1 is Guild Leader)
            if targetRank <= myRank then
                return false, "Cannot remove member with equal or higher guild rank!"
            end
        end
    elseif action == "Exempt [LOA]" then
        if not DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_NOTE_EDIT) then
            return false, "Missing guild permission: Edit Member Notes."
        end
    end

    return true, nil, mIdx
end

function FR:ApplyAuditAction(m, action, isBatch)
    if not m or not m.name then return end
    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)
    local guildName = GetGuildName(guildId)
    action = action or "Warn Mail"

    local allowed, errMsg, mIdx = self:CheckAuditActionPermission(guildId, m.name, action)
    if not allowed then
        self.PrintChat(string.format("|cFF5555Error:|r Cannot perform '%s' on %s: %s", action, m.name, errMsg or "Not allowed."))
        return
    end

    if action == "Warn Mail" then
        self:RecordMemberMailSent(m.name, "warn")
        self:TriggerInactivityMailHandoff(m.name, m.days)
        self.PrintChat(string.format("Staged warning mail for %s. (Recorded in mail history)", ColorText(m.name, "00FFCC")))
        if not isBatch then
            self:RenderAuditorRows()
        end

    elseif action == "Kick & Mail" then
        local function DoKickAndMail()
            SCENE_MANAGER:Show("mailSend")
            zo_callLater(function()
                ZO_MailSendToField:SetText(m.name)
                ZO_MailSendSubjectField:SetText(string.format("[%s] Roster Space Update", guildName))
                ZO_MailSendBodyField:SetText(string.format("Greetings %s,\n\nThank you for being part of %s! As our trading roster is currently full, we had to open up your space to keep trades flowing while you take a break. You are always warmly welcome back whenever you return to Tamriel—simply message any officer or re-apply!\n\nWarm regards,\n%s Staff",
                    m.name, guildName, guildName))
                ZO_MailSendBodyField:TakeFocus()
            end, 200)

            FR:RecordMemberMailSent(m.name, "kick_mail")
            GuildRemove(guildId, m.name)
            FR.PrintChat(string.format("|c59E08ARemoved:|r %s from %s and staged courtesy mail.", ColorText(m.name, "00FFCC"), ColorText(guildName, "FF9900")))

            if not isBatch then
                zo_callLater(function()
                    FR:RunRosterAudit()
                    FR:UpdateAuditorUI()
                end, 500)
            end
        end

        if isBatch then
            DoKickAndMail()
        else
            RegisterAuditorCustomDialogs()
            ZO_Dialogs_ShowDialog("FISSAL_CONFIRM_KICK_AND_MAIL", {
                onConfirm = DoKickAndMail,
            }, {
                mainTextParams = { m.name, guildName }
            })
        end

    elseif action == "Kick Only" then
        local function DoKickOnly()
            GuildRemove(guildId, m.name)
            FR.PrintChat(string.format("|c59E08ARemoved:|r %s from %s.", ColorText(m.name, "00FFCC"), ColorText(guildName, "FF9900")))

            if not isBatch then
                zo_callLater(function()
                    FR:RunRosterAudit()
                    FR:UpdateAuditorUI()
                end, 500)
            end
        end

        if isBatch then
            DoKickOnly()
        else
            RegisterAuditorCustomDialogs()
            ZO_Dialogs_ShowDialog("FISSAL_CONFIRM_KICK_MEMBER", {
                onConfirm = DoKickOnly,
            }, {
                mainTextParams = { m.name, guildName }
            })
        end

    elseif action == "Exempt [LOA]" then
        if mIdx and mIdx > 0 then
            local curNote = m.note or ""
            local newNote = curNote ~= "" and (curNote .. " [LOA]") or "[LOA]"
            SetGuildMemberNote(guildId, mIdx, newNote)
            self.PrintChat(string.format("Added [LOA] exemption tag to %s's note.", ColorText(m.name, "00FFCC")))
            if not isBatch then
                zo_callLater(function()
                    FR:RunRosterAudit()
                    FR:UpdateAuditorUI()
                end, 500)
            end
        else
            self.PrintChat(string.format("|cFF5555Error:|r Could not find member index for %s to edit note.", m.name))
        end

    elseif action == "Void / Hide" then
        if not self.savedVars.auditExclusions then self.savedVars.auditExclusions = {} end
        if not self.savedVars.auditExclusions[guildId] then self.savedVars.auditExclusions[guildId] = {} end
        local clean = string.gsub(string.lower(m.name), "^@", "")
        self.savedVars.auditExclusions[guildId][clean] = {
            displayName = m.name,
            reason = "Staff Void",
            date = GetTimeStamp(),
        }
        self.PrintChat(string.format("|c59E08AVoided:|r Added %s to permanent audit exclusion list.", ColorText(m.name, "00FFCC")))
        if not isBatch then
            self:RunRosterAudit()
            self:UpdateAuditorUI()
            if self.auditExclusionsDrawer and not self.auditExclusionsDrawer:IsHidden() then
                self:RefreshAuditExclusionsDrawer()
            end
        end
    end
end

--[[ =========================================================================
     PERMANENT VOID / EXCLUSIONS REGISTRY DRAWER
========================================================================= ]]--

function FR:BuildAuditExclusionsDrawer(card)
    local wm = WINDOW_MANAGER
    local drawer = wm:CreateControl("$(parent)_ExclusionsDrawer", card, CT_BACKDROP)
    drawer:SetAnchor(TOPLEFT, card, TOPLEFT, 4, 4)
    drawer:SetAnchor(BOTTOMRIGHT, card, BOTTOMRIGHT, -4, -4)
    drawer:SetCenterColor(0.03, 0.03, 0.05, 1.0)
    drawer:SetEdgeColor(0.60, 0.35, 0.85, 0.95)
    drawer:SetEdgeTexture("", 8, 1, 0)
    drawer:SetDrawTier(DT_HIGH)
    drawer:SetDrawLayer(DL_OVERLAY)
    drawer:SetDrawLevel(10)
    drawer:SetMouseEnabled(true)
    drawer:SetHandler("OnMouseWheel", function() end)
    drawer:SetHidden(true)
    self.auditExclusionsDrawer = drawer

    -- Core ESO Default Backdrop for 100% solid opacity
    local defBg = wm:CreateControlFromVirtual("$(parent)_DefBg", drawer, "ZO_DefaultBackdrop")
    defBg:SetAnchorFill()
    defBg:SetAlpha(1.0)
    defBg:SetDrawLayer(DL_BACKGROUND)

    -- Midnight Munge Texture plate
    local munge = wm:CreateControl("$(parent)_Munge", drawer, CT_TEXTURE)
    munge:SetAnchorFill()
    munge:SetTexture("EsoUI/Art/Performance/StatusMeterMunge.dds")
    munge:SetAlpha(0.96)
    munge:SetDrawLayer(DL_BACKGROUND)
    munge:SetDrawLevel(1)

    local titleLbl = wm:CreateControl("$(parent)_Title", drawer, CT_LABEL)
    titleLbl:SetAnchor(TOPLEFT, drawer, TOPLEFT, 16, 12)
    titleLbl:SetFont("ZoFontGameBold")
    titleLbl:SetText("|cFF9900PERMANENT VOID / EXCLUSIONS REGISTRY|r  |c00FFCC(Shielded Accounts)|r")

    local descLbl = wm:CreateControl("$(parent)_Desc", drawer, CT_LABEL)
    descLbl:SetAnchor(TOPLEFT, titleLbl, BOTTOMLEFT, 0, 4)
    descLbl:SetFont("ZoFontGameSmall")
    descLbl:SetText("|cAAAAAAMembers listed here are permanently excluded from inactivity purges, check-ins, and warnings.|r")

    local closeBtn = wm:CreateControl("$(parent)_CloseBtn", drawer, CT_BUTTON)
    closeBtn:SetAnchor(TOPRIGHT, drawer, TOPRIGHT, -12, 10)
    closeBtn:SetDimensions(28, 22)
    closeBtn:SetFont("ZoFontGameBold")
    closeBtn:SetText("X")
    self:StyleTactileButton(closeBtn, {
        normalBg = { 0.15, 0.05, 0.05, 0.85 },
        hoverBg = { 0.30, 0.08, 0.08, 0.95 },
        normalEdge = { 0.60, 0.20, 0.20, 0.80 },
        hoverEdge = { 1.00, 0.30, 0.30, 1.00 },
        normalTextColor = { 1, 0.5, 0.5, 1 },
        hoverTextColor = { 1, 0.8, 0.8, 1 },
        tooltipTitle = "Close Exclusions Registry",
    })
    closeBtn:SetHandler("OnClicked", function()
        self:CloseAuditExclusionsDrawer()
    end)

    local headerY = 54
    local colHeader = wm:CreateControl("$(parent)_Header", drawer, CT_BACKDROP)
    colHeader:SetAnchor(TOPLEFT, drawer, TOPLEFT, 12, headerY)
    colHeader:SetAnchor(TOPRIGHT, drawer, TOPRIGHT, -12, headerY)
    colHeader:SetHeight(24)
    colHeader:SetCenterColor(0.08, 0.06, 0.12, 0.95)
    colHeader:SetEdgeColor(0.40, 0.25, 0.55, 0.70)
    colHeader:SetEdgeTexture("", 8, 1, 0)

    local h1 = wm:CreateControl("$(parent)_H1", colHeader, CT_LABEL)
    h1:SetAnchor(LEFT, colHeader, LEFT, 16, 0)
    h1:SetFont("ZoFontGameBold")
    h1:SetText(ColorText("EXCLUDED ACCOUNT", "FF9900"))

    local h2 = wm:CreateControl("$(parent)_H2", colHeader, CT_LABEL)
    h2:SetAnchor(LEFT, colHeader, LEFT, 240, 0)
    h2:SetFont("ZoFontGameBold")
    h2:SetText(ColorText("DATE ADDED", "00FFCC"))

    local h3 = wm:CreateControl("$(parent)_H3", colHeader, CT_LABEL)
    h3:SetAnchor(LEFT, colHeader, LEFT, 400, 0)
    h3:SetFont("ZoFontGameBold")
    h3:SetText(ColorText("REASON", "FFD700"))

    local h4 = wm:CreateControl("$(parent)_H4", colHeader, CT_LABEL)
    h4:SetAnchor(RIGHT, colHeader, RIGHT, -30, 0)
    h4:SetFont("ZoFontGameBold")
    h4:SetText(ColorText("MANAGEMENT", "59E08A"))

    self.auditExclusionsRows = {}
    local EXCL_ROWS = 10
    local startY = headerY + 28
    for i = 1, EXCL_ROWS do
        local row = wm:CreateControl("$(parent)_Row_" .. i, drawer, CT_BACKDROP)
        row:SetAnchor(TOPLEFT, drawer, TOPLEFT, 12, startY + (i - 1) * 32)
        row:SetAnchor(TOPRIGHT, drawer, TOPRIGHT, -12, startY + (i - 1) * 32)
        row:SetHeight(30)
        row:SetCenterColor(0.05, 0.04, 0.08, 0.85)
        row:SetEdgeColor(0.25, 0.18, 0.35, 0.50)
        row:SetEdgeTexture("", 8, 1, 0)

        local nameLbl = wm:CreateControl("$(parent)_Name", row, CT_LABEL)
        nameLbl:SetAnchor(LEFT, row, LEFT, 16, 0)
        nameLbl:SetDimensions(210, 22)
        nameLbl:SetFont("ZoFontGameBold")
        nameLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        nameLbl:SetText("@Member")
        row.nameLbl = nameLbl

        local dateLbl = wm:CreateControl("$(parent)_Date", row, CT_LABEL)
        dateLbl:SetAnchor(LEFT, row, LEFT, 240, 0)
        dateLbl:SetDimensions(140, 22)
        dateLbl:SetFont("ZoFontGame")
        dateLbl:SetText("--")
        row.dateLbl = dateLbl

        local reasonLbl = wm:CreateControl("$(parent)_Reason", row, CT_LABEL)
        reasonLbl:SetAnchor(LEFT, row, LEFT, 400, 0)
        reasonLbl:SetDimensions(240, 22)
        reasonLbl:SetFont("ZoFontGame")
        reasonLbl:SetText("Staff Void")
        row.reasonLbl = reasonLbl

        local unhideBtn = wm:CreateControl("$(parent)_UnhideBtn", row, CT_BUTTON)
        unhideBtn:SetAnchor(RIGHT, row, RIGHT, -14, 0)
        unhideBtn:SetDimensions(130, 22)
        unhideBtn:SetFont("ZoFontGameSmall")
        unhideBtn:SetText("Unhide / Restore")
        self:StyleTactileButton(unhideBtn, {
            normalBg = { 0.06, 0.12, 0.08, 0.85 },
            hoverBg = { 0.10, 0.20, 0.14, 0.95 },
            normalEdge = { 0.20, 0.70, 0.35, 0.80 },
            hoverEdge = { 0.30, 1.00, 0.50, 1.00 },
            normalTextColor = { 0.3, 1, 0.5, 1 },
            hoverTextColor = { 0.6, 1, 0.7, 1 },
            tooltipTitle = "Restore Account",
            tooltipText = "Remove this member from permanent void exclusion and return them to standard auditing.",
        })
        row.unhideBtn = unhideBtn

        self.auditExclusionsRows[i] = row
    end

    local footerY = -12
    local countLbl = wm:CreateControl("$(parent)_CountLbl", drawer, CT_LABEL)
    countLbl:SetAnchor(BOTTOMLEFT, drawer, BOTTOMLEFT, 16, footerY)
    countLbl:SetFont("ZoFontGameSmall")
    countLbl:SetText("Total Excluded: 0")
    self.auditExclCountLbl = countLbl

    local nextBtn = wm:CreateControl("$(parent)_NextBtn", drawer, CT_BUTTON)
    nextBtn:SetAnchor(BOTTOMRIGHT, drawer, BOTTOMRIGHT, -14, footerY + 2)
    nextBtn:SetDimensions(60, 20)
    nextBtn:SetFont("ZoFontGameSmall")
    nextBtn:SetText("Next >")
    self:StyleTactileButton(nextBtn, {
        normalBg = { 0.06, 0.06, 0.09, 0.85 },
        hoverBg = { 0.08, 0.16, 0.18, 0.95 },
        normalEdge = { 0.25, 0.25, 0.30, 0.60 },
        hoverEdge = { 0, 0.90, 0.80, 1.0 },
        normalTextColor = { 0.7, 0.7, 0.7, 1 },
        hoverTextColor = { 0, 1, 0.9, 1 },
    })
    nextBtn:SetHandler("OnClicked", function()
        local total = self.auditExclTotalCount or 0
        local maxPages = math.max(1, math.ceil(total / 10))
        if (self.auditExclPage or 1) < maxPages then
            self.auditExclPage = (self.auditExclPage or 1) + 1
            self:RefreshAuditExclusionsDrawer()
        end
    end)
    self.auditExclNextBtn = nextBtn

    local pageLbl = wm:CreateControl("$(parent)_PageLbl", drawer, CT_LABEL)
    pageLbl:SetAnchor(RIGHT, nextBtn, LEFT, -8, 0)
    pageLbl:SetFont("ZoFontGameSmall")
    pageLbl:SetText("Page 1/1")
    self.auditExclPageLbl = pageLbl

    local prevBtn = wm:CreateControl("$(parent)_PrevBtn", drawer, CT_BUTTON)
    prevBtn:SetAnchor(RIGHT, pageLbl, LEFT, -8, 0)
    prevBtn:SetDimensions(60, 20)
    prevBtn:SetFont("ZoFontGameSmall")
    prevBtn:SetText("< Prev")
    self:StyleTactileButton(prevBtn, {
        normalBg = { 0.06, 0.06, 0.09, 0.85 },
        hoverBg = { 0.08, 0.16, 0.18, 0.95 },
        normalEdge = { 0.25, 0.25, 0.30, 0.60 },
        hoverEdge = { 0, 0.90, 0.80, 1.0 },
        normalTextColor = { 0.7, 0.7, 0.7, 1 },
        hoverTextColor = { 0, 1, 0.9, 1 },
    })
    prevBtn:SetHandler("OnClicked", function()
        if (self.auditExclPage or 1) > 1 then
            self.auditExclPage = (self.auditExclPage or 1) - 1
            self:RefreshAuditExclusionsDrawer()
        end
    end)
    self.auditExclPrevBtn = prevBtn
end

function FR:SetAuditTableHidden(hidden)
    if self.auditColHeader then self.auditColHeader:SetHidden(hidden) end
    if self.auditRows then
        for _, r in ipairs(self.auditRows) do
            if r.row then r.row:SetHidden(hidden) end
        end
    end
    if self.auditExportBtn then self.auditExportBtn:SetHidden(hidden) end
    if self.auditPrevBtn then self.auditPrevBtn:SetHidden(hidden) end
    if self.auditPageLbl then self.auditPageLbl:SetHidden(hidden) end
    if self.auditNextBtn then self.auditNextBtn:SetHidden(hidden) end
    if self.auditStatusSummaryLbl then self.auditStatusSummaryLbl:SetHidden(hidden) end
    if self.auditFilterControls then
        for _, ctrl in ipairs(self.auditFilterControls) do
            if ctrl then ctrl:SetHidden(hidden) end
        end
    end
end

function FR:OpenAuditExclusionsDrawer()
    if not self.auditExclusionsDrawer then return end
    if self.auditSearchBox then self.auditSearchBox:LoseFocus() end
    self.auditExclPage = 1
    self:SetAuditTableHidden(true)
    self:RefreshAuditExclusionsDrawer()
    self.auditExclusionsDrawer:SetHidden(false)
end

function FR:CloseAuditExclusionsDrawer()
    if not self.auditExclusionsDrawer then return end
    self.auditExclusionsDrawer:SetHidden(true)
    self:SetAuditTableHidden(false)
    if self.auditPendingRender then
        self.auditPendingRender = false
        self:RenderAuditorRows()
    else
        self:UpdateAuditorUI()
    end
end

function FR:ToggleAuditExclusionsDrawer()
    if not self.auditExclusionsDrawer then return end
    if self.auditExclusionsDrawer:IsHidden() then
        self:OpenAuditExclusionsDrawer()
    else
        self:CloseAuditExclusionsDrawer()
    end
end

function FR:RefreshAuditExclusionsDrawer()
    if not self.auditExclusionsDrawer then return end
    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)

    local excls = {}
    if self.savedVars and self.savedVars.auditExclusions and self.savedVars.auditExclusions[guildId] then
        for cleanName, data in pairs(self.savedVars.auditExclusions[guildId]) do
            table.insert(excls, {
                clean = cleanName,
                displayName = data.displayName or ("@" .. cleanName),
                reason = data.reason or "Staff Void",
                date = data.date or 0,
            })
        end
    end

    table.sort(excls, function(a, b)
        return (a.date or 0) > (b.date or 0)
    end)

    local total = #excls
    self.auditExclTotalCount = total
    local maxPages = math.max(1, math.ceil(total / 10))
    if not self.auditExclPage or self.auditExclPage > maxPages then self.auditExclPage = maxPages end
    if self.auditExclPage < 1 then self.auditExclPage = 1 end

    local startIndex = (self.auditExclPage - 1) * 10
    for i = 1, 10 do
        local row = self.auditExclusionsRows and self.auditExclusionsRows[i]
        local idx = startIndex + i
        if row then
            if idx <= total then
                local entry = excls[idx]
                row:SetHidden(false)
                row.nameLbl:SetText(string.format("|c00FFCC%s|r", entry.displayName))

                local dateStr = "Unknown"
                if entry.date and entry.date > 0 then
                    local ago = GetTimeStamp() - entry.date
                    if ago < 3600 then dateStr = string.format("%dm ago", math.floor(ago / 60))
                    elseif ago < 86400 then dateStr = string.format("%dh ago", math.floor(ago / 3600))
                    else dateStr = string.format("%dd ago", math.floor(ago / 86400)) end
                end
                row.dateLbl:SetText(string.format("|c888888%s|r", dateStr))
                row.reasonLbl:SetText(string.format("|cFFD700%s|r", entry.reason))

                row.unhideBtn:SetHandler("OnClicked", function()
                    if FR.savedVars and FR.savedVars.auditExclusions and FR.savedVars.auditExclusions[guildId] then
                        FR.savedVars.auditExclusions[guildId][entry.clean] = nil
                        FR.PrintChat(string.format("|c59E08ARestored:|r %s returned to standard auditing.", ColorText(entry.displayName, "00FFCC")))
                        FR:RunRosterAudit()
                        FR:UpdateAuditorUI()
                        FR:RefreshAuditExclusionsDrawer()
                    end
                end)
            else
                row:SetHidden(true)
            end
        end
    end

    if self.auditExclCountLbl then
        self.auditExclCountLbl:SetText(string.format("Total Excluded: |c00FFCC%d|r member(s)", total))
    end
    if self.auditExclPageLbl then
        self.auditExclPageLbl:SetText(string.format("Page %d/%d", self.auditExclPage, maxPages))
    end
end

--[[ =========================================================================
     SAFE PACED BATCH AUTO-PROCESSOR
========================================================================= ]]--

function FR:BuildAuditBatchProgressPanel(card)
    local wm = WINDOW_MANAGER
    local batchPanel = wm:CreateControl("$(parent)_BatchProgress", card, CT_BACKDROP)
    batchPanel:SetAnchor(BOTTOMLEFT, card, BOTTOMLEFT, 10, -32)
    batchPanel:SetAnchor(BOTTOMRIGHT, card, BOTTOMRIGHT, -10, -32)
    batchPanel:SetHeight(28)
    batchPanel:SetCenterColor(0.12, 0.08, 0.02, 0.98)
    batchPanel:SetEdgeColor(1.00, 0.65, 0.15, 0.95)
    batchPanel:SetEdgeTexture("", 8, 1, 0)
    batchPanel:SetHidden(true)
    self.auditBatchProgressPanel = batchPanel

    local statusLbl = wm:CreateControl("$(parent)_StatusLbl", batchPanel, CT_LABEL)
    statusLbl:SetAnchor(LEFT, batchPanel, LEFT, 12, 0)
    statusLbl:SetFont("ZoFontGameBold")
    statusLbl:SetText("[BATCH] Processing: Initializing...")
    self.auditBatchStatusLbl = statusLbl

    local abortBtn = wm:CreateControl("$(parent)_AbortBtn", batchPanel, CT_BUTTON)
    abortBtn:SetAnchor(RIGHT, batchPanel, RIGHT, -8, 0)
    abortBtn:SetDimensions(110, 22)
    abortBtn:SetFont("ZoFontGameBold")
    abortBtn:SetText("STOP / ABORT")
    self:StyleTactileButton(abortBtn, {
        normalBg = { 0.25, 0.05, 0.05, 0.95 },
        hoverBg = { 0.40, 0.08, 0.08, 1.00 },
        normalEdge = { 0.90, 0.20, 0.20, 0.90 },
        hoverEdge = { 1.00, 0.35, 0.35, 1.00 },
        normalTextColor = { 1, 0.8, 0.8, 1 },
        hoverTextColor = { 1, 1, 1, 1 },
        tooltipTitle = "Emergency Abort",
        tooltipText = "Immediately halt the active batch processing queue.",
    })
    abortBtn:SetHandler("OnClicked", function()
        self:AbortAuditBatch()
    end)
    self.auditBatchAbortBtn = abortBtn
end

function FR:StartAuditBatch()
    if self.isAuditBatchRunning then
        self.PrintChat("|cFF5555Error:|r Audit batch is already running!")
        return
    end

    local members = self.auditFilteredMembers or {}
    if #members == 0 then
        self.PrintChat("|cFFCC00Notice:|r No flagged members in current audit filter to batch process.")
        return
    end

    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)
    local guildName = GetGuildName(guildId)

    local queue = {}
    local warnCount, kickMailCount, kickCount, loaCount, voidCount = 0, 0, 0, 0, 0
    for _, m in ipairs(members) do
        local act = m.stagedAction or "Warn Mail"
        table.insert(queue, { member = m, action = act })
        if act == "Warn Mail" then warnCount = warnCount + 1
        elseif act == "Kick & Mail" then kickMailCount = kickMailCount + 1
        elseif act == "Kick Only" then kickCount = kickCount + 1
        elseif act == "Exempt [LOA]" then loaCount = loaCount + 1
        elseif act == "Void / Hide" then voidCount = voidCount + 1
        end
    end

    local total = #queue
    local summaryStr = string.format("Batch process |c00FFCC%d|r members for |cFF9900%s|r?\n\n|cCCCCCCPlanned Actions:|r\n• Warn Mail: |cFFCC00%d|r\n• Kick & Mail: |cFF6600%d|r\n• Kick Only: |cFF4444%d|r\n• Exempt [LOA]: |c59E08A%d|r\n• Void / Hide: |cAA66FF%d|r\n\n|cFF9900Actions execute sequentially with 1.5s safe pacing to prevent server throttling.|r",
        total, guildName, warnCount, kickMailCount, kickCount, loaCount, voidCount)

    RegisterAuditorCustomDialogs()
    ZO_Dialogs_ShowDialog("FISSAL_CONFIRM_AUDIT_BATCH", {
        onConfirm = function()
            FR:ExecuteAuditBatchQueue(guildId, queue)
        end,
    }, {
        mainTextParams = { summaryStr }
    })
end

function FR:AbortAuditBatch()
    if not self.isAuditBatchRunning then return end
    self.isAuditBatchRunning = false
    EVENT_MANAGER:UnregisterForUpdate("FissalRelay_AuditBatchPacer")
    if self.auditBatchProgressPanel then
        self.auditBatchProgressPanel:SetHidden(true)
    end
    self.PrintChat("|cFF5555Audit Batch ABORTED by user.|r")
    self:RunRosterAudit()
    self:UpdateAuditorUI()
end

function FR:ExecuteAuditBatchQueue(guildId, queue)
    if not queue or #queue == 0 then return end
    self.isAuditBatchRunning = true
    self.auditBatchQueue = queue
    self.auditBatchIndex = 0
    self.auditBatchTotal = #queue

    if self.auditBatchProgressPanel then
        self.auditBatchProgressPanel:SetHidden(false)
    end

    local function ProcessNext()
        if not self.isAuditBatchRunning then return end
        self.auditBatchIndex = self.auditBatchIndex + 1

        if self.auditBatchIndex > self.auditBatchTotal then
            self.isAuditBatchRunning = false
            EVENT_MANAGER:UnregisterForUpdate("FissalRelay_AuditBatchPacer")
            if self.auditBatchProgressPanel then
                self.auditBatchProgressPanel:SetHidden(true)
            end
            self.PrintChat(string.format("|c59E08ABatch Processing Complete:|r Successfully executed actions for %d member(s).", self.auditBatchTotal))
            self:RunRosterAudit()
            self:UpdateAuditorUI()
            return
        end

        local item = self.auditBatchQueue[self.auditBatchIndex]
        if item and item.member then
            if self.auditBatchStatusLbl then
                self.auditBatchStatusLbl:SetText(string.format("[BATCH] Processing (%d/%d): |c00FFCC%s|r [%s]...",
                    self.auditBatchIndex, self.auditBatchTotal, item.member.name, item.action))
            end
            self:ApplyAuditAction(item.member, item.action, true)
        end
    end

    ProcessNext()
    EVENT_MANAGER:UnregisterForUpdate("FissalRelay_AuditBatchPacer")
    EVENT_MANAGER:RegisterForUpdate("FissalRelay_AuditBatchPacer", 1500, ProcessNext)
end

function FR:RenderAuditorRows()
    if self.auditExclusionsDrawer and not self.auditExclusionsDrawer:IsHidden() then
        self.auditPendingRender = true
        return
    end
    local members = self.auditFilteredMembers or {}
    local total = #members
    local maxPages = math.max(1, math.ceil(total / ROWS_PER_PAGE))
    if self.auditCurrentPage > maxPages then self.auditCurrentPage = maxPages end
    if self.auditCurrentPage < 1 then self.auditCurrentPage = 1 end

    local startIndex = (self.auditCurrentPage - 1) * ROWS_PER_PAGE
    local gIdx = self.selectedGuildIndex or 1
    local guildId = GetGuildId(gIdx)

    for r = 1, ROWS_PER_PAGE do
        local row = self.auditRows and self.auditRows[r]
        local memberIndex = startIndex + r

        if row then
            if memberIndex <= total then
                local m = members[memberIndex]
                row:SetHidden(false)

                local mailCount, lastMailTime = self:GetMemberMailInfo(m.name)
                local noteLower = string.lower(m.note or "")
                local isPerm = string.find(noteLower, "perm") or string.find(noteLower, "founder") or string.find(noteLower, "core") or string.find(noteLower, "vip") or string.find(noteLower, "staff")

                -- Name
                row.nameLbl:SetText(m.name)
                row.nameLbl:SetMouseEnabled(true)
                row.nameLbl:SetHandler("OnMouseEnter", function(ctrl)
                    InitializeTooltip(InformationTooltip, ctrl, TOP, 0, -4)
                    local sDetails = FR.GetMemberDetailedSales and FR:GetMemberDetailedSales(guildId, m.name)
                    local lines = {
                        string.format("|c00FFCC%s|r  |c888888(Rank: %s • %d days offline)|r", m.name, m.rank, m.days),
                    }
                    if m.note and m.note ~= "" then
                        table.insert(lines, string.format("|cFFD700Member Note:|r |cFFFFFF%s|r", m.note))
                    end

                    if mailCount > 0 then
                        local agoStr = "just now"
                        local ago = GetTimeStamp() - lastMailTime
                        if ago < 3600 then agoStr = string.format("%dm ago", math.floor(ago / 60))
                        elseif ago < 86400 then agoStr = string.format("%dh ago", math.floor(ago / 3600))
                        else agoStr = string.format("%dd ago", math.floor(ago / 86400)) end
                        table.insert(lines, string.format("|c888888Mail History:|r |cFFCC00✉ %d time(s) mailed (last: %s)|r", mailCount, agoStr))
                    else
                        table.insert(lines, "|c888888Mail History:|r |c888888Never mailed check-in|r")
                    end

                    table.insert(lines, "")
                    table.insert(lines, "|cE6C387Trade Velocity Breakdown:|r")
                    if sDetails then
                        table.insert(lines, string.format("• Current Trade Week: |c59E08A%d sales|r (|cFFD700%sg|r)",
                            sDetails.thisWeekCount, ZO_LocalizeDecimalNumber(sDetails.thisWeekGold)))
                        table.insert(lines, string.format("• Prior Trade Week:   |c59E08A%d sales|r (|cFFD700%sg|r)",
                            sDetails.priorWeekCount, ZO_LocalizeDecimalNumber(sDetails.priorWeekGold)))
                        table.insert(lines, string.format("• Total Recorded:     |c00FFCC%d sales|r (|cFFD700%sg|r)",
                            sDetails.totalCount, ZO_LocalizeDecimalNumber(sDetails.totalGold)))
                        if sDetails.lastSaleTs > 0 then
                            local ago = GetTimeStamp() - sDetails.lastSaleTs
                            local agoStr = (ago < 86400) and string.format("%dh ago", math.floor(ago / 3600)) or string.format("%dd ago", math.floor(ago / 86400))
                            table.insert(lines, string.format("• Last Recorded Sale: |c00FFCC%s|r", agoStr))
                        else
                            table.insert(lines, "• Last Recorded Sale: |c888888None recorded|r")
                        end
                    else
                        table.insert(lines, string.format("• Recorded Sales: |c59E08A%d|r (|cFFD700%sg|r)", m.salesCount or 0, ZO_LocalizeDecimalNumber(m.salesGold or 0)))
                    end

                    table.insert(lines, "")
                    table.insert(lines, "|cE6C387Treasury & Dues Status:|r")
                    table.insert(lines, string.format("• Bank Deposits: |cFFAA00%sg|r", ZO_LocalizeDecimalNumber(m.deposits or 0)))
                    local duesColor = m.duesMet and "59E08A" or "FF5555"
                    table.insert(lines, string.format("• Dues Status: |c%s%s (%s)|r", duesColor, m.duesMet and "DUES MET" or "MISSING DUES", m.duesReason or "Unmet"))

                    if isPerm then
                        table.insert(lines, "")
                        table.insert(lines, "|c00FFCC🛡 PROTECTED: Permanent member note keyword detected!|r")
                    end

                    SetTooltipText(InformationTooltip, table.concat(lines, "\n"))
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

                -- Note / Shield / Mail column
                if isPerm then
                    row.noteLbl:SetText("|c00FFCC🛡 PERM|r")
                elseif mailCount > 0 then
                    row.noteLbl:SetText(string.format("|cFFCC00✉ %dx|r", mailCount))
                elseif m.note and m.note ~= "" then
                    local cleanNote = string.gsub(m.note, "\n", " ")
                    if #cleanNote > 10 then cleanNote = string.sub(cleanNote, 1, 8) .. ".." end
                    row.noteLbl:SetText(string.format("|cFFD700%s|r", cleanNote))
                else
                    row.noteLbl:SetText("|c444444--|r")
                end

                row.noteLbl:SetMouseEnabled(true)
                row.noteLbl:SetHandler("OnMouseEnter", function(ctrl)
                    InitializeTooltip(InformationTooltip, ctrl, TOP, 0, -4)
                    local tip = string.format("|c00FFCC%s|r\n|cFFD700Note:|r %s\n|c888888Mail Count:|r %d time(s) mailed",
                        m.name, (m.note and m.note ~= "") and m.note or "None", mailCount)
                    if isPerm then tip = tip .. "\n|c00FFCC🛡 PROTECTED: Permanent member note keyword detected!|r" end
                    SetTooltipText(InformationTooltip, tip)
                end)
                row.noteLbl:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)

                -- Dynamic Action button sync
                row.memberData = m
                local stagedAction = m.stagedAction or "Warn Mail"
                row.selectedAction = stagedAction
                for idx, act in ipairs(AUDIT_ACTIONS) do
                    if act == stagedAction then
                        row.actionIndex = idx
                        break
                    end
                end
                ApplyAuditActionStyle(row.actionBtn, stagedAction)

                -- Apply button handler
                row.applyBtn:SetHandler("OnClicked", function()
                    self:ApplyAuditAction(m, row.selectedAction or "Warn Mail")
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
        if not self.auditShieldActiveSellers then
            self.auditStatLbl:SetText(string.format("|cFF5555⚠ WARNING: Seller Shield OFF (Appear-Offline sellers listed)!|r Inactive: |cFF5555%d|r | Excused: %d",
                total, self.auditExcusedCount or 0))
        else
            self.auditStatLbl:SetText(string.format("Roster: %d | Inactive: |cFF5555%d|r | Shielded Active: |c59E08A%d|r | Excused: %d",
                self.auditTotalMembers or 0, total, self.auditShieldedCount or 0, self.auditExcusedCount or 0))
        end
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
