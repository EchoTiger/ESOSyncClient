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
FR.auditSearchQuery = ""
FR.auditCurrentPage = 1
FR.auditFilteredMembers = {}

--[[ =========================================================================
     AUDITOR DATA ENGINE
========================================================================= ]]--

function FR:RunRosterAudit()
    local gIdx = self.selectedGuildIndex or 1
    local numGuilds = GetNumGuilds()
    if gIdx < 1 or gIdx > numGuilds then return end

    local guildId = GetGuildId(gIdx)
    local guildName = GetGuildName(guildId)
    local memberCount = GetNumGuildMembers(guildId)
    local cutoffSecs = (self.auditFilterDays or 14) * 86400

    -- Build deposit lookup for this guild (case-insensitive)
    local depositsByMember = {}
    if self.savedVars and self.savedVars.staff and self.savedVars.staff.bankDeposits then
        for _, dep in pairs(self.savedVars.staff.bankDeposits) do
            if dep.guildId == guildId and dep.depositor then
                local lowerDep = string.lower(dep.depositor)
                depositsByMember[lowerDep] = (depositsByMember[lowerDep] or 0) + (dep.amount or 0)
            end
        end
    end

    local rawInactives = {}
    local excusedCount = 0
    local officerCount = 0

    for m = 1, memberCount do
        local name, note, rankIndex, playerStatus, secsSinceLogoff = GetGuildMemberInfo(guildId, m)
        local isOffline = (playerStatus == PLAYER_STATUS_OFFLINE) or (secsSinceLogoff and secsSinceLogoff > 0)

        if isOffline and secsSinceLogoff and secsSinceLogoff >= cutoffSecs then
            local days = math.floor(secsSinceLogoff / 86400)
            local rankName = GetGuildRankCustomName(guildId, rankIndex)
            local isLeaderOrOfficer = (rankIndex and rankIndex <= 2)

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
                local lowerName = string.lower(name or "")
                table.insert(rawInactives, {
                    name = name or "@Unknown",
                    note = note or "",
                    rank = cleanRank,
                    rankIndex = rankIndex or 0,
                    days = days,
                    isLOA = isLOA,
                    isOfficer = isLeaderOrOfficer,
                    deposits = depositsByMember[lowerName] or 0,
                })
            end
        end
    end

    table.sort(rawInactives, function(a, b)
        return a.days > b.days
    end)

    self.auditFilteredMembers = rawInactives
    self.auditTotalMembers = memberCount
    self.auditExcusedCount = excusedCount
    self.auditOfficerCount = officerCount

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
    offToggle:SetAnchor(TOPLEFT, card, TOPLEFT, 275, 7)
    offToggle:SetDimensions(120, 22)
    offToggle:SetFont("ZoFontGameSmall")
    offToggle:SetText("Exclude Officers")
    self:StyleTactileButton(offToggle, {
        normalBg = { 0.04, 0.12, 0.08, 0.85 },
        hoverBg = { 0.06, 0.18, 0.12, 0.95 },
        normalEdge = { 0.20, 0.75, 0.35, 0.80 },
        hoverEdge = { 0.30, 1.00, 0.50, 1.00 },
        normalTextColor = { 0.3, 1, 0.5, 1 },
        hoverTextColor = { 0.6, 1, 0.7, 1 },
        tooltipTitle = "Exclude Officers",
        tooltipText = "Hide Guild Master and Officer ranks (Ranks 1 & 2) from inactivity warnings and purge candidates.",
    })
    offToggle:SetHandler("OnClicked", function()
        self.auditExcludeOfficers = not self.auditExcludeOfficers
        self.auditCurrentPage = 1
        self:UpdateAuditorUI()
    end)
    self.auditOffToggle = offToggle

    -- Toggle: Exclude LOA
    local loaToggle = wm:CreateControl("$(parent)_LoaToggle", card, CT_BUTTON)
    loaToggle:SetAnchor(TOPLEFT, card, TOPLEFT, 405, 7)
    loaToggle:SetDimensions(110, 22)
    loaToggle:SetFont("ZoFontGameSmall")
    loaToggle:SetText("Exclude [LOA]")
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

    -- Search Box Container
    local searchBg = wm:CreateControlFromVirtual("$(parent)_SearchBg", card, "ZO_EditBackdrop")
    searchBg:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 6)
    searchBg:SetDimensions(150, 24)

    local searchBox = wm:CreateControlFromVirtual("$(parent)_Search", searchBg, "ZO_DefaultEditForBackdrop")
    searchBox:SetAnchorFill()
    searchBox:SetFont("ZoFontGameSmall")
    searchBox:SetTextType(TEXT_TYPE_ALL)
    searchBox:SetDefaultText("Search member...")
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
    h1:SetText(ColorText("MEMBER HANDLE", "FF9900"))

    local h2 = wm:CreateControl("$(parent)_H2", colHeader, CT_LABEL)
    h2:SetAnchor(LEFT, colHeader, LEFT, 190, 0)
    h2:SetFont("ZoFontGameBold")
    h2:SetText(ColorText("RANK", "00FFCC"))

    local h3 = wm:CreateControl("$(parent)_H3", colHeader, CT_LABEL)
    h3:SetAnchor(LEFT, colHeader, LEFT, 310, 0)
    h3:SetFont("ZoFontGameBold")
    h3:SetText(ColorText("OFFLINE", "FFD700"))

    local h4 = wm:CreateControl("$(parent)_H4", colHeader, CT_LABEL)
    h4:SetAnchor(LEFT, colHeader, LEFT, 390, 0)
    h4:SetFont("ZoFontGameBold")
    h4:SetText(ColorText("BANK DUES", "59E08A"))

    local h5 = wm:CreateControl("$(parent)_H5", colHeader, CT_LABEL)
    h5:SetAnchor(LEFT, colHeader, LEFT, 490, 0)
    h5:SetFont("ZoFontGameBold")
    h5:SetText(ColorText("NOTE", "FFFFFF"))

    local h6 = wm:CreateControl("$(parent)_H6", colHeader, CT_LABEL)
    h6:SetAnchor(RIGHT, colHeader, RIGHT, -20, 0)
    h6:SetFont("ZoFontGameBold")
    h6:SetText(ColorText("ACTION", "FF9900"))

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
        rankLbl:SetAnchor(LEFT, row, LEFT, 190, 0)
        rankLbl:SetFont("ZoFontGameSmall")
        rankLbl:SetText("Member")
        row.rankLbl = rankLbl

        local daysLbl = wm:CreateControl("$(parent)_Days", row, CT_LABEL)
        daysLbl:SetAnchor(LEFT, row, LEFT, 310, 0)
        daysLbl:SetFont("ZoFontGameBold")
        daysLbl:SetText("14d")
        row.daysLbl = daysLbl

        local duesLbl = wm:CreateControl("$(parent)_Dues", row, CT_LABEL)
        duesLbl:SetAnchor(LEFT, row, LEFT, 390, 0)
        duesLbl:SetFont("ZoFontGameSmall")
        duesLbl:SetText("0g")
        row.duesLbl = duesLbl

        local noteLbl = wm:CreateControl("$(parent)_Note", row, CT_LABEL)
        noteLbl:SetAnchor(LEFT, row, LEFT, 490, 0)
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
    statLbl:SetAnchor(BOTTOMLEFT, card, BOTTOMLEFT, 12, footerY)
    statLbl:SetFont("ZoFontGameSmall")
    statLbl:SetText("Audit: Calculating...")
    self.auditStatLbl = statLbl

    -- Page controls
    local nextBtn = wm:CreateControl("$(parent)_NextBtn", card, CT_BUTTON)
    nextBtn:SetAnchor(BOTTOMRIGHT, card, BOTTOMRIGHT, -12, footerY)
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
        local maxPages = math.max(1, math.ceil(#self.auditFilteredMembers / ROWS_PER_PAGE))
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
        tooltipText = "Print a clean summary of inactive members and their recorded bank deposits to chat and SavedVariables for Discord export.",
    })
    exportBtn:SetHandler("OnClicked", function()
        self:ExportAuditToChat()
    end)
end

--[[ =========================================================================
     AUDITOR RENDERING & ACTIONS
========================================================================= ]]--

function FR:UpdateAuditorUI()
    self:RunRosterAudit()

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
                    SetTooltipText(InformationTooltip, string.format("|c00FFCC%s|r\n|c888888Rank: %s\nOffline: %d days\nRecorded Bank Deposits: %sg|r",
                        m.name, m.rank, m.days, ZO_LocalizeDecimalNumber(m.deposits or 0)))
                end)
                row.nameLbl:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)

                -- Rank
                row.rankLbl:SetText(m.rank)

                -- Offline duration
                local dayColor = m.days >= 30 and "FF5555" or (m.days >= 14 and "FFAA00" or "FFD700")
                row.daysLbl:SetText(string.format("|c%s%d days|r", dayColor, m.days))

                -- Bank Dues
                local depText = m.deposits > 0 and string.format("|c59E08A%sg|r", ZO_LocalizeDecimalNumber(m.deposits)) or "|c8888880g|r"
                row.duesLbl:SetText(depText)

                -- Note
                local cleanNote = string.gsub(m.note or "", "\n", " ")
                if #cleanNote > 16 then
                    cleanNote = string.sub(cleanNote, 1, 14) .. ".."
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
        self.auditPageLbl:SetText(string.format("Page %d/%d (%d inactives)", self.auditCurrentPage, maxPages, total))
    end

    if self.auditStatLbl then
        self.auditStatLbl:SetText(string.format("Roster: %d total | Inactive (%dd+): |cFF5555%d|r | Excused: %d",
            self.auditTotalMembers or 0, self.auditFilterDays or 14, total, self.auditExcusedCount or 0))
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
        df("  - %s (%d days offline, %s) | Bank: %sg",
            m.name, m.days, m.rank, ZO_LocalizeDecimalNumber(m.deposits or 0))
    end
    if #members > count then
        df("  ...and %d more inactive members saved to SavedVariables.", #members - count)
    end
end
