--[[
    FissalRelay_Bids.lua
    Kiosk Recon & Bids Vault for Fissal Relay Prime
    Crafted by Echo & Fissal for Fissal Relay and the Redfur Guilds.

    Features:
      • Comprehensive Kiosk Bids Ledger tracking all guild kiosk bid submissions,
        direct purchases, and refunds parsed from LibHistoire banked currency events
      • Dynamic Kiosk Status Resolver (Won, Active Bid, Refunded, Closed) eliminating
        stale pending badges past trade-week reset
      • Multi-Guild filter tabs ([All Guilds], [Post], [Caravan], [Dealers])
      • Trade-Week Round filtering ([Active Round], [Prior Round], [All History])
      • Sortable column headers (Guild, Trader, Bidder, Amount, Status, Date)
      • Financial Telemetry Ribbon summarizing bids placed, total committed gold, and refunds
      • Ground Recon Vault cataloging scouted trader kiosks across Tamriel
      • Mouse wheel scrolling and smooth pagination
]]--

FissalRelay = FissalRelay or {}
local FR = FissalRelay

local ROWS_PER_PAGE = 10

FR.bidsSubView = "bids" -- "bids" or "recon"
FR.bidsGuildFilter = "ALL" -- "ALL" or specific guildId (number)
FR.bidsRoundFilter = "CURRENT" -- "CURRENT", "PRIOR", or "ALL"
FR.bidsSortCol = "timestamp"
FR.bidsSortAsc = false
FR.bidsCurrentPage = 1
FR.bidsList = {}
FR.reconList = {}

--[[ =========================================================================
     DATA GATHERING & STATUS RESOLUTION
========================================================================= ]]--

function FR:CollectBidsData()
    local rawBids = {}
    if self.savedVars and self.savedVars.staff and self.savedVars.staff.bids then
        for id, b in pairs(self.savedVars.staff.bids) do
            table.insert(rawBids, b)
        end
    end

    local now = GetTimeStamp()
    local curWeekStart, curWeekEnd = 0, 0
    if self.GetTuesdayTradeWeek then
        curWeekStart, curWeekEnd = self:GetTuesdayTradeWeek(now)
    else
        curWeekStart = now - (7 * 86400)
        curWeekEnd = now + (7 * 86400)
    end
    local priorWeekStart = curWeekStart - 604800
    local priorWeekEnd = curWeekStart

    local filtered = {}
    local roundFilt = self.bidsRoundFilter or "CURRENT"
    local guildFilt = self.bidsGuildFilter or "ALL"

    for _, b in ipairs(rawBids) do
        local ts = tonumber(b.timestamp) or 0
        local matchGuild = (guildFilt == "ALL") or (b.guildId == guildFilt)
        local matchRound = true

        if roundFilt == "CURRENT" then
            matchRound = (ts >= curWeekStart)
        elseif roundFilt == "PRIOR" then
            matchRound = (ts >= priorWeekStart and ts < priorWeekEnd)
        elseif roundFilt == "ALL" then
            matchRound = true
        end

        if matchGuild and matchRound then
            -- Resolve status dynamically
            local statusText, statusColor = "Pending", "FFCC00"
            if self.ResolveBidStatus then
                statusText, statusColor = self:ResolveBidStatus(b)
            else
                statusText = b.status or "Pending"
                statusColor = (statusText == "Won" and "59E08A") or (string.find(statusText, "Refund") and "888888") or "FFCC00"
            end

            local resolvedBid = {
                id = b.id,
                timestamp = ts,
                guildId = b.guildId,
                guildName = b.guildName or (b.guildId and GetGuildName(b.guildId)) or "Guild",
                bidder = b.bidder or "Staff",
                kioskName = b.kioskName or "Unknown Kiosk",
                amount = tonumber(b.amount) or 0,
                statusText = statusText,
                statusColor = statusColor,
                rawStatus = b.status,
                refundTime = b.refundTime,
            }
            table.insert(filtered, resolvedBid)
        end
    end

    -- Sort data
    local col = self.bidsSortCol or "timestamp"
    local asc = self.bidsSortAsc or false

    table.sort(filtered, function(a, b)
        local valA, valB
        if col == "guild" then
            valA = a.guildName:lower()
            valB = b.guildName:lower()
        elseif col == "kiosk" then
            valA = a.kioskName:lower()
            valB = b.kioskName:lower()
        elseif col == "bidder" then
            valA = a.bidder:lower()
            valB = b.bidder:lower()
        elseif col == "amount" then
            valA = a.amount
            valB = b.amount
        elseif col == "status" then
            valA = a.statusText:lower()
            valB = b.statusText:lower()
        else
            valA = a.timestamp
            valB = b.timestamp
        end

        if valA == valB then
            return a.timestamp > b.timestamp
        end
        if asc then
            return valA < valB
        else
            return valA > valB
        end
    end)

    self.bidsList = filtered
end

function FR:CollectReconData()
    local recon = {}
    if self.savedVars and self.savedVars.kiosks then
        for trader, k in pairs(self.savedVars.kiosks) do
            table.insert(recon, k)
        end
    end
    table.sort(recon, function(a, b)
        return (a.timestamp or 0) > (b.timestamp or 0)
    end)
    self.reconList = recon
end

--[[ =========================================================================
     UI CONSTRUCTION
========================================================================= ]]--

function FR:BuildBidsReconUI(parent)
    local wm = WINDOW_MANAGER
    local panel = wm:CreateControl("FissalRelay_Console_Tab4", parent, CT_CONTROL)
    panel:SetAnchorFill()
    panel:SetHidden(true)
    self.consoleTabs[4] = panel

    -- 1. Main Backdrop Card
    local card = wm:CreateControl("$(parent)_Card", panel, CT_BACKDROP)
    card:SetAnchorFill()
    card:SetCenterColor(0.06, 0.06, 0.08, 0.85)
    card:SetEdgeColor(0.30, 0.25, 0.18, 0.70)
    card:SetEdgeTexture("", 8, 1, 0)
    card:SetMouseEnabled(true)
    card:SetHandler("OnMouseWheel", function(control, delta)
        local isBids = (self.bidsSubView == "bids")
        local activeList = isBids and self.bidsList or self.reconList
        local maxPages = math.max(1, math.ceil(#activeList / ROWS_PER_PAGE))
        if delta < 0 and self.bidsCurrentPage < maxPages then
            self.bidsCurrentPage = self.bidsCurrentPage + 1
            self:RenderBidsRows()
        elseif delta > 0 and self.bidsCurrentPage > 1 then
            self.bidsCurrentPage = self.bidsCurrentPage - 1
            self:RenderBidsRows()
        end
    end)

    -- 2. Sub-View Switcher Bar (Bids Ledger vs Ground Recon)
    local bidsBtn = wm:CreateControl("$(parent)_BidsBtn", card, CT_BUTTON)
    bidsBtn:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 6)
    bidsBtn:SetDimensions(140, 24)
    bidsBtn:SetFont("ZoFontGameBold")
    bidsBtn:SetText("Kiosk Bids Ledger")
    self:StyleTactileButton(bidsBtn, {
        normalBg = { 0.06, 0.06, 0.09, 0.85 },
        hoverBg = { 0.14, 0.12, 0.06, 0.95 },
        normalEdge = { 0.35, 0.28, 0.18, 0.60 },
        hoverEdge = { 0.90, 0.70, 0.20, 1.0 },
        normalTextColor = { 0.7, 0.7, 0.7, 1 },
        hoverTextColor = { 1, 0.9, 0.4, 1 },
        tooltipTitle = "Kiosk Bids Ledger",
        tooltipText = "View all guild kiosk bid submissions, won kiosks, and bid refunds parsed from LibHistoire banked currency logs.",
    })
    bidsBtn:SetHandler("OnClicked", function()
        self.bidsSubView = "bids"
        self.bidsCurrentPage = 1
        self:UpdateBidsUI()
    end)
    self.bidsTabBtn = bidsBtn

    local reconBtn = wm:CreateControl("$(parent)_ReconBtn", card, CT_BUTTON)
    reconBtn:SetAnchor(LEFT, bidsBtn, RIGHT, 8, 0)
    reconBtn:SetDimensions(140, 24)
    reconBtn:SetFont("ZoFontGameBold")
    reconBtn:SetText("Ground Recon Vault")
    self:StyleTactileButton(reconBtn, {
        normalBg = { 0.06, 0.06, 0.09, 0.85 },
        hoverBg = { 0.08, 0.16, 0.18, 0.95 },
        normalEdge = { 0.25, 0.25, 0.30, 0.60 },
        hoverEdge = { 0, 0.90, 0.80, 1.0 },
        normalTextColor = { 0.7, 0.7, 0.7, 1 },
        hoverTextColor = { 0, 1, 0.9, 1 },
        tooltipTitle = "Ground Recon Vault",
        tooltipText = "View all trader kiosks scouted in-person across Tamriel, with zone, city, and merchant details.",
    })
    reconBtn:SetHandler("OnClicked", function()
        self.bidsSubView = "recon"
        self.bidsCurrentPage = 1
        self:UpdateBidsUI()
    end)
    self.reconTabBtn = reconBtn

    local scoutNowBtn = wm:CreateControl("$(parent)_ScoutNowBtn", card, CT_BUTTON)
    scoutNowBtn:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 6)
    scoutNowBtn:SetDimensions(150, 24)
    scoutNowBtn:SetFont("ZoFontGameSmall")
    scoutNowBtn:SetText("Scout Current Kiosk")
    self:StyleTactileButton(scoutNowBtn, {
        normalBg = { 0.04, 0.12, 0.12, 0.90 },
        hoverBg = { 0.06, 0.18, 0.18, 0.98 },
        normalEdge = { 0, 0.75, 0.65, 0.80 },
        hoverEdge = { 0, 1.0, 0.85, 1.0 },
        normalTextColor = { 0, 1, 0.8, 1 },
        hoverTextColor = { 0.4, 1, 0.9, 1 },
        tooltipTitle = "Scout Current Kiosk",
        tooltipText = "Record an instant reconnaissance log of the guild trader you are currently interacting with in the world.",
    })
    scoutNowBtn:SetHandler("OnClicked", function()
        local recorded = self:RecordKioskObservation()
        if recorded then
            self.PrintChat("Ground recon observation captured!")
            self:UpdateBidsUI()
        else
            self.PrintChat("No trader interaction active. Open a trader store to scout.")
        end
    end)

    -- 3. Secondary Filter Bar (Guild Filter Tabs & Trade-Week Cycler)
    local filterBar = wm:CreateControl("$(parent)_FilterBar", card, CT_CONTROL)
    filterBar:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 34)
    filterBar:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 34)
    filterBar:SetHeight(28)
    self.bidsFilterBar = filterBar

    -- Guild Filter Buttons: [All Guilds] [Post] [Caravan] [Dealers] ...
    self.bidsGuildBtns = {}
    local allGuildBtn = wm:CreateControl("$(parent)_G_ALL", filterBar, CT_BUTTON)
    allGuildBtn:SetAnchor(LEFT, filterBar, LEFT, 0, 0)
    allGuildBtn:SetDimensions(80, 22)
    allGuildBtn:SetFont("ZoFontGameSmall")
    allGuildBtn:SetText("All Guilds")
    self:StyleTactileButton(allGuildBtn, {
        normalBg = { 0.08, 0.08, 0.12, 0.85 },
        hoverBg = { 0.12, 0.18, 0.22, 0.95 },
        normalEdge = { 0.30, 0.30, 0.35, 0.65 },
        hoverEdge = { 0, 0.85, 0.75, 1.0 },
    })
    allGuildBtn:SetHandler("OnClicked", function()
        self.bidsGuildFilter = "ALL"
        self.bidsCurrentPage = 1
        self:UpdateBidsFilterButtons()
        self:UpdateBidsUI()
    end)
    self.bidsGuildBtns["ALL"] = allGuildBtn

    local prevCtrl = allGuildBtn
    local numGuilds = GetNumGuilds()
    for g = 1, math.min(numGuilds, 4) do
        local gId = GetGuildId(g)
        local gName = GetGuildName(gId)
        local shortName = gName
        if string.find(string.lower(gName), "post") then shortName = "Post"
        elseif string.find(string.lower(gName), "caravan") then shortName = "Caravan"
        elseif string.find(string.lower(gName), "dealer") then shortName = "Dealers"
        else
            shortName = string.sub(gName, 1, 10)
        end

        local gBtn = wm:CreateControl("$(parent)_G_" .. gId, filterBar, CT_BUTTON)
        gBtn:SetAnchor(LEFT, prevCtrl, RIGHT, 6, 0)
        gBtn:SetDimensions(75, 22)
        gBtn:SetFont("ZoFontGameSmall")
        gBtn:SetText(shortName)
        self:StyleTactileButton(gBtn, {
            normalBg = { 0.08, 0.08, 0.12, 0.85 },
            hoverBg = { 0.12, 0.18, 0.22, 0.95 },
            normalEdge = { 0.30, 0.30, 0.35, 0.65 },
            hoverEdge = { 0, 0.85, 0.75, 1.0 },
            tooltipTitle = gName,
            tooltipText = "Filter kiosk bids exclusively for " .. gName,
        })
        gBtn:SetHandler("OnClicked", function()
            self.bidsGuildFilter = gId
            self.bidsCurrentPage = 1
            self:UpdateBidsFilterButtons()
            self:UpdateBidsUI()
        end)
        self.bidsGuildBtns[gId] = gBtn
        prevCtrl = gBtn
    end

    -- Round / Week Filters on the Right: [Active Round] [Prior Round] [All Rounds]
    local allRoundsBtn = wm:CreateControl("$(parent)_R_ALL", filterBar, CT_BUTTON)
    allRoundsBtn:SetAnchor(RIGHT, filterBar, RIGHT, 0, 0)
    allRoundsBtn:SetDimensions(75, 22)
    allRoundsBtn:SetFont("ZoFontGameSmall")
    allRoundsBtn:SetText("All History")
    self:StyleTactileButton(allRoundsBtn, {
        normalBg = { 0.08, 0.08, 0.12, 0.85 },
        hoverBg = { 0.14, 0.12, 0.06, 0.95 },
        normalEdge = { 0.30, 0.30, 0.35, 0.65 },
        hoverEdge = { 0.95, 0.70, 0.20, 1.0 },
    })
    allRoundsBtn:SetHandler("OnClicked", function()
        self.bidsRoundFilter = "ALL"
        self.bidsCurrentPage = 1
        self:UpdateBidsFilterButtons()
        self:UpdateBidsUI()
    end)
    self.bidsRoundBtns = self.bidsRoundBtns or {}
    self.bidsRoundBtns["ALL"] = allRoundsBtn

    local priorRoundBtn = wm:CreateControl("$(parent)_R_PRIOR", filterBar, CT_BUTTON)
    priorRoundBtn:SetAnchor(RIGHT, allRoundsBtn, LEFT, -6, 0)
    priorRoundBtn:SetDimensions(85, 22)
    priorRoundBtn:SetFont("ZoFontGameSmall")
    priorRoundBtn:SetText("Prior Round")
    self:StyleTactileButton(priorRoundBtn, {
        normalBg = { 0.08, 0.08, 0.12, 0.85 },
        hoverBg = { 0.14, 0.12, 0.06, 0.95 },
        normalEdge = { 0.30, 0.30, 0.35, 0.65 },
        hoverEdge = { 0.95, 0.70, 0.20, 1.0 },
        tooltipTitle = "Prior Week Bids",
        tooltipText = "View bids submitted for the previous Tuesday reset round.",
    })
    priorRoundBtn:SetHandler("OnClicked", function()
        self.bidsRoundFilter = "PRIOR"
        self.bidsCurrentPage = 1
        self:UpdateBidsFilterButtons()
        self:UpdateBidsUI()
    end)
    self.bidsRoundBtns["PRIOR"] = priorRoundBtn

    local activeRoundBtn = wm:CreateControl("$(parent)_R_CURRENT", filterBar, CT_BUTTON)
    activeRoundBtn:SetAnchor(RIGHT, priorRoundBtn, LEFT, -6, 0)
    activeRoundBtn:SetDimensions(90, 22)
    activeRoundBtn:SetFont("ZoFontGameBold")
    activeRoundBtn:SetText("Active Round")
    self:StyleTactileButton(activeRoundBtn, {
        normalBg = { 0.04, 0.14, 0.14, 0.90 },
        hoverBg = { 0.06, 0.20, 0.20, 0.98 },
        normalEdge = { 0, 0.75, 0.65, 0.85 },
        hoverEdge = { 0, 1.00, 0.90, 1.00 },
        tooltipTitle = "Current Week Bids",
        tooltipText = "View bids placed for the upcoming or newly resolved Tuesday reset round.",
    })
    activeRoundBtn:SetHandler("OnClicked", function()
        self.bidsRoundFilter = "CURRENT"
        self.bidsCurrentPage = 1
        self:UpdateBidsFilterButtons()
        self:UpdateBidsUI()
    end)
    self.bidsRoundBtns["CURRENT"] = activeRoundBtn

    -- 4. Financial Telemetry Ribbon
    local ribbon = wm:CreateControl("$(parent)_Ribbon", card, CT_BACKDROP)
    ribbon:SetAnchor(TOPLEFT, card, TOPLEFT, 10, 64)
    ribbon:SetAnchor(TOPRIGHT, card, TOPRIGHT, -10, 64)
    ribbon:SetHeight(24)
    ribbon:SetCenterColor(0.08, 0.07, 0.05, 0.80)
    ribbon:SetEdgeColor(0.40, 0.30, 0.15, 0.50)
    ribbon:SetEdgeTexture("", 8, 1, 0)
    self.bidsRibbon = ribbon

    local ribLbl = wm:CreateControl("$(parent)_RibLbl", ribbon, CT_LABEL)
    ribLbl:SetAnchor(LEFT, ribbon, LEFT, 8, 0)
    ribLbl:SetFont("ZoFontGameSmall")
    ribLbl:SetText("• Bids: --  |  Committed: --  |  Won: --  |  Refunded: --")
    self.bidsRibbonLbl = ribLbl

    -- 5. Sortable Column Header Row
    local headerY = 92
    local colHeader = wm:CreateControl("$(parent)_Header", card, CT_BACKDROP)
    colHeader:SetAnchor(TOPLEFT, card, TOPLEFT, 10, headerY)
    colHeader:SetAnchor(TOPRIGHT, card, TOPRIGHT, -10, headerY)
    colHeader:SetHeight(24)
    colHeader:SetCenterColor(0.10, 0.10, 0.14, 0.95)
    colHeader:SetEdgeColor(0.25, 0.20, 0.15, 0.65)
    colHeader:SetEdgeTexture("", 8, 1, 0)

    local function MakeSortHeader(name, anchorCtrl, anchorPoint, toPoint, x, width, text, sortKey)
        local btn = wm:CreateControl("$(parent)_" .. name, colHeader, CT_BUTTON)
        btn:SetAnchor(anchorPoint, anchorCtrl, toPoint, x, 0)
        btn:SetDimensions(width, 22)
        btn:SetFont("ZoFontGameBold")
        btn:SetNormalFontColor(0.8, 0.75, 0.65, 1)
        btn:SetText(text)
        btn:SetHandler("OnClicked", function()
            if self.bidsSortCol == sortKey then
                self.bidsSortAsc = not self.bidsSortAsc
            else
                self.bidsSortCol = sortKey
                self.bidsSortAsc = false
            end
            self:UpdateHeaderArrows()
            self:CollectBidsData()
            self:RenderBidsRows()
        end)
        return btn
    end

    self.bidsH1 = MakeSortHeader("H1", colHeader, LEFT, LEFT, 8, 125, "GUILD ▲", "guild")
    self.bidsH2 = MakeSortHeader("H2", self.bidsH1, LEFT, RIGHT, 6, 235, "KIOSK TRADER & LOCATION", "kiosk")
    self.bidsH3 = MakeSortHeader("H3", self.bidsH2, LEFT, RIGHT, 6, 105, "BIDDER", "bidder")
    self.bidsH4 = MakeSortHeader("H4", self.bidsH3, LEFT, RIGHT, 6, 115, "AMOUNT", "amount")
    self.bidsH5 = MakeSortHeader("H5", self.bidsH4, LEFT, RIGHT, 6, 135, "STATUS", "status")
    self.bidsH6 = MakeSortHeader("H6", self.bidsH5, LEFT, RIGHT, 6, 95, "DATE / AGE", "timestamp")

    -- 6. Table Rows (10 Spacious Rows with Alternating Shading)
    self.bidsRows = {}
    local rowStartY = headerY + 26
    local rowHeight = 27

    for r = 1, ROWS_PER_PAGE do
        local row = wm:CreateControl("$(parent)_Row_" .. r, card, CT_BACKDROP)
        row:SetAnchor(TOPLEFT, card, TOPLEFT, 10, rowStartY + (r - 1) * (rowHeight + 2))
        row:SetAnchor(TOPRIGHT, card, TOPRIGHT, -10, rowStartY + (r - 1) * (rowHeight + 2))
        row:SetHeight(rowHeight)
        row:SetCenterColor(0.04, 0.04, 0.06, 0.60)
        row:SetEdgeColor(0.18, 0.16, 0.12, 0.40)
        row:SetEdgeTexture("", 8, 1, 0)
        row:SetMouseEnabled(true)

        local col1 = wm:CreateControl("$(parent)_Col1", row, CT_LABEL)
        col1:SetAnchor(LEFT, row, LEFT, 8, 0)
        col1:SetDimensions(125, 20)
        col1:SetFont("ZoFontGameMedium")
        col1:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        col1:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        row.col1 = col1

        local col2 = wm:CreateControl("$(parent)_Col2", row, CT_LABEL)
        col2:SetAnchor(LEFT, col1, RIGHT, 6, 0)
        col2:SetDimensions(235, 20)
        col2:SetFont("ZoFontGameSmall")
        col2:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        col2:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        row.col2 = col2

        local col3 = wm:CreateControl("$(parent)_Col3", row, CT_LABEL)
        col3:SetAnchor(LEFT, col2, RIGHT, 6, 0)
        col3:SetDimensions(105, 20)
        col3:SetFont("ZoFontGameSmall")
        col3:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        col3:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        row.col3 = col3

        local col4 = wm:CreateControl("$(parent)_Col4", row, CT_LABEL)
        col4:SetAnchor(LEFT, col3, RIGHT, 6, 0)
        col4:SetDimensions(115, 20)
        col4:SetFont("ZoFontGameBold")
        col4:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        row.col4 = col4

        local col5 = wm:CreateControl("$(parent)_Col5", row, CT_LABEL)
        col5:SetAnchor(LEFT, col4, RIGHT, 6, 0)
        col5:SetDimensions(135, 20)
        col5:SetFont("ZoFontGameSmall")
        col5:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        row.col5 = col5

        local col6 = wm:CreateControl("$(parent)_Col6", row, CT_LABEL)
        col6:SetAnchor(LEFT, col5, RIGHT, 6, 0)
        col6:SetAnchor(RIGHT, row, RIGHT, -8, 0)
        col6:SetFont("ZoFontGameSmall")
        col6:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        row.col6 = col6

        row:SetHandler("OnMouseEnter", function(control)
            control:SetCenterColor(0.10, 0.10, 0.16, 0.90)
            control:SetEdgeColor(0.90, 0.70, 0.20, 0.85)
            if control.bidData then
                local b = control.bidData
                InitializeTooltip(InformationTooltip, control, RIGHT, 5, 0)
                InformationTooltip:AddLine(string.format("|c00FFCC%s|r", b.kioskName), "ZoFontGameBold")
                InformationTooltip:AddLine(string.format("• Guild: |cFFFFFF%s|r", b.guildName), "ZoFontGameSmall")
                InformationTooltip:AddLine(string.format("• Bid Amount: |cFFD700%s gold|r", ZO_LocalizeDecimalNumber(b.amount)), "ZoFontGameSmall")
                InformationTooltip:AddLine(string.format("• Bidder: |cFFFFFF%s|r", b.bidder), "ZoFontGameSmall")
                InformationTooltip:AddLine(string.format("• Status: |c%s%s|r", b.statusColor or "FFFFFF", b.statusText or "Pending"), "ZoFontGameSmall")
                if b.refundTime and b.refundTime > 0 then
                    local refAgo = ZO_FormatDurationAgo(GetTimeStamp() - b.refundTime)
                    InformationTooltip:AddLine(string.format("• Refunded: |c888888%s ago|r", refAgo), "ZoFontGameSmall")
                end
                local dateStr = os.date("%A, %b %d at %H:%M UTC", b.timestamp)
                InformationTooltip:AddLine(string.format("• Timestamp: |c888888%s|r", dateStr), "ZoFontGameSmall")
            end
        end)
        row:SetHandler("OnMouseExit", function(control)
            local idx = control.rowIndex or 1
            if idx % 2 == 0 then
                control:SetCenterColor(0.04, 0.04, 0.06, 0.60)
            else
                control:SetCenterColor(0.03, 0.03, 0.04, 0.40)
            end
            control:SetEdgeColor(0.18, 0.16, 0.12, 0.40)
            ClearTooltip(InformationTooltip)
        end)

        self.bidsRows[r] = row
    end

    -- 7. Footer & Pagination Controls
    local footerY = -8
    local statLbl = wm:CreateControl("$(parent)_StatLbl", card, CT_LABEL)
    statLbl:SetAnchor(BOTTOMLEFT, card, BOTTOMLEFT, 12, footerY)
    statLbl:SetFont("ZoFontGameSmall")
    statLbl:SetText("Bids Ledger: Initializing...")
    self.bidsStatLbl = statLbl

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
        tooltipText = "View next page of records.",
    })
    nextBtn:SetHandler("OnClicked", function()
        local activeList = (self.bidsSubView == "bids") and self.bidsList or self.reconList
        local maxPages = math.max(1, math.ceil(#activeList / ROWS_PER_PAGE))
        if self.bidsCurrentPage < maxPages then
            self.bidsCurrentPage = self.bidsCurrentPage + 1
            self:RenderBidsRows()
        end
    end)

    local pageLbl = wm:CreateControl("$(parent)_PageLbl", card, CT_LABEL)
    pageLbl:SetAnchor(RIGHT, nextBtn, LEFT, -8, 0)
    pageLbl:SetFont("ZoFontGameSmall")
    pageLbl:SetText("Page 1/1")
    self.bidsPageLbl = pageLbl

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
        tooltipText = "View previous page of records.",
    })
    prevBtn:SetHandler("OnClicked", function()
        if self.bidsCurrentPage > 1 then
            self.bidsCurrentPage = self.bidsCurrentPage - 1
            self:RenderBidsRows()
        end
    end)
end

--[[ =========================================================================
     FILTER & HEADER BUTTON HIGHLIGHTS
========================================================================= ]]--

function FR:UpdateBidsFilterButtons()
    -- Guild Buttons
    for id, btn in pairs(self.bidsGuildBtns or {}) do
        if btn.bg then
            if id == self.bidsGuildFilter then
                btn.isCustomActive = true
                btn.bg:SetCenterColor(0.18, 0.12, 0.04, 0.95)
                btn.bg:SetEdgeColor(0.95, 0.70, 0.15, 1.0)
                btn:SetNormalFontColor(1, 0.85, 0.2, 1)
            else
                btn.isCustomActive = false
                btn.bg:SetCenterColor(0.08, 0.08, 0.12, 0.85)
                btn.bg:SetEdgeColor(0.30, 0.30, 0.35, 0.65)
                btn:SetNormalFontColor(0.7, 0.7, 0.7, 1)
            end
        end
    end

    -- Round Buttons
    for id, btn in pairs(self.bidsRoundBtns or {}) do
        if btn.bg then
            if id == self.bidsRoundFilter then
                btn.isCustomActive = true
                btn.bg:SetCenterColor(0.04, 0.18, 0.16, 0.95)
                btn.bg:SetEdgeColor(0, 0.90, 0.80, 1.0)
                btn:SetNormalFontColor(0, 1, 0.85, 1)
            else
                btn.isCustomActive = false
                btn.bg:SetCenterColor(0.08, 0.08, 0.12, 0.85)
                btn.bg:SetEdgeColor(0.30, 0.30, 0.35, 0.65)
                btn:SetNormalFontColor(0.7, 0.7, 0.7, 1)
            end
        end
    end
end

function FR:UpdateHeaderArrows()
    local arrow = self.bidsSortAsc and " ▲" or " ▼"
    local col = self.bidsSortCol

    if self.bidsH1 then self.bidsH1:SetText("GUILD" .. (col == "guild" and arrow or "")) end
    if self.bidsH2 then self.bidsH2:SetText("KIOSK TRADER & LOCATION" .. (col == "kiosk" and arrow or "")) end
    if self.bidsH3 then self.bidsH3:SetText("BIDDER" .. (col == "bidder" and arrow or "")) end
    if self.bidsH4 then self.bidsH4:SetText("AMOUNT" .. (col == "amount" and arrow or "")) end
    if self.bidsH5 then self.bidsH5:SetText("STATUS" .. (col == "status" and arrow or "")) end
    if self.bidsH6 then self.bidsH6:SetText("DATE" .. (col == "timestamp" and arrow or "")) end
end

--[[ =========================================================================
     RENDERING & LOGIC
========================================================================= ]]--

function FR:UpdateBidsUI()
    self:CollectBidsData()
    self:CollectReconData()
    self:UpdateBidsFilterButtons()
    self:UpdateHeaderArrows()

    local isBids = (self.bidsSubView == "bids")

    -- Toggle Sub-view Tab Buttons
    if self.bidsTabBtn and self.bidsTabBtn.bg then
        if isBids then
            self.bidsTabBtn.isCustomActive = true
            self.bidsTabBtn.bg:SetCenterColor(0.18, 0.12, 0.04, 0.95)
            self.bidsTabBtn.bg:SetEdgeColor(0.95, 0.70, 0.15, 1.0)
            self.bidsTabBtn:SetNormalFontColor(1, 0.85, 0.2, 1)
        else
            self.bidsTabBtn.isCustomActive = false
            self.bidsTabBtn.bg:SetCenterColor(0.06, 0.06, 0.09, 0.85)
            self.bidsTabBtn.bg:SetEdgeColor(0.35, 0.28, 0.18, 0.60)
            self.bidsTabBtn:SetNormalFontColor(0.65, 0.65, 0.65, 1)
        end
    end
    if self.reconTabBtn and self.reconTabBtn.bg then
        if not isBids then
            self.reconTabBtn.isCustomActive = true
            self.reconTabBtn.bg:SetCenterColor(0.04, 0.15, 0.16, 0.95)
            self.reconTabBtn.bg:SetEdgeColor(0, 0.90, 0.80, 1.0)
            self.reconTabBtn:SetNormalFontColor(0, 1, 0.8, 1)
        else
            self.reconTabBtn.isCustomActive = false
            self.reconTabBtn.bg:SetCenterColor(0.06, 0.06, 0.09, 0.85)
            self.reconTabBtn.bg:SetEdgeColor(0.25, 0.25, 0.30, 0.60)
            self.reconTabBtn:SetNormalFontColor(0.6, 0.6, 0.6, 1)
        end
    end

    -- Toggle filter bar and ribbon visibility based on view
    if self.bidsFilterBar then self.bidsFilterBar:SetHidden(not isBids) end
    if self.bidsRibbon then self.bidsRibbon:SetHidden(not isBids) end

    -- Update column header labels
    if isBids then
        self:UpdateHeaderArrows()
    else
        if self.bidsH1 then self.bidsH1:SetText("TRADER NPC") end
        if self.bidsH2 then self.bidsH2:SetText("CITY / ZONE") end
        if self.bidsH3 then self.bidsH3:SetText("STATUS") end
        if self.bidsH4 then self.bidsH4:SetText("CONTROLLING GUILD") end
        if self.bidsH5 then self.bidsH5:SetText("VERIFIED") end
        if self.bidsH6 then self.bidsH6:SetText("SCOUTED") end
    end

    -- Financial Telemetry calculation
    if isBids and self.bidsRibbonLbl then
        local totalBids = #self.bidsList
        local totalCommitted = 0
        local totalRefunded = 0
        local wonTrader = "None"

        for _, b in ipairs(self.bidsList) do
            totalCommitted = totalCommitted + b.amount
            if string.find(b.statusText, "Won") then
                wonTrader = b.kioskName
            elseif string.find(b.statusText, "Refund") then
                totalRefunded = totalRefunded + b.amount
            end
        end

        local roundLabel = (self.bidsRoundFilter == "CURRENT" and "Active Round")
            or (self.bidsRoundFilter == "PRIOR" and "Prior Round")
            or "All History"

        self.bidsRibbonLbl:SetText(string.format(
            "• |cE6C387%s:|r |cFFFFFF%d|r bids  |  Committed: |cFFD700%sg|r  |  Won: |c59E08A%s|r  |  Refunded: |c888888%sg|r",
            roundLabel, totalBids, ZO_LocalizeDecimalNumber(totalCommitted), wonTrader, ZO_LocalizeDecimalNumber(totalRefunded)
        ))
    end

    self:RenderBidsRows()
end

function FR:RenderBidsRows()
    local isBids = (self.bidsSubView == "bids")
    local list = isBids and (self.bidsList or {}) or (self.reconList or {})
    local total = #list
    local maxPages = math.max(1, math.ceil(total / ROWS_PER_PAGE))
    if self.bidsCurrentPage > maxPages then self.bidsCurrentPage = maxPages end
    if self.bidsCurrentPage < 1 then self.bidsCurrentPage = 1 end

    local startIndex = (self.bidsCurrentPage - 1) * ROWS_PER_PAGE

    for r = 1, ROWS_PER_PAGE do
        local row = self.bidsRows and self.bidsRows[r]
        local itemIndex = startIndex + r

        if row then
            row.rowIndex = r
            if itemIndex <= total then
                local item = list[itemIndex]
                row:SetHidden(false)
                row.bidData = isBids and item or nil

                if r % 2 == 0 then
                    row:SetCenterColor(0.04, 0.04, 0.06, 0.60)
                else
                    row:SetCenterColor(0.03, 0.03, 0.04, 0.40)
                end

                if isBids then
                    -- Bids item
                    row.col1:SetText(string.format("|c00FFCC%s|r", item.guildName or "Guild"))
                    row.col2:SetText(item.kioskName or "Unknown Kiosk")
                    row.col3:SetText(string.format("|cCCCCCC%s|r", item.bidder or "Staff"))
                    row.col4:SetText(string.format("|cFFD700%sg|r", ZO_LocalizeDecimalNumber(item.amount or 0)))
                    row.col5:SetText(string.format("|c%s● %s|r", item.statusColor or "FFFFFF", item.statusText or "Pending"))

                    local timeAgo = item.timestamp and ZO_FormatDurationAgo(GetTimeStamp() - item.timestamp) or "--"
                    row.col6:SetText(timeAgo)
                else
                    -- Recon item
                    row.col1:SetText(string.format("|cFFFFFF%s|r", item.trader or "Trader"))
                    row.col2:SetText(string.format("%s, %s", item.city or "?", item.zone or "?"))
                    row.col3:SetText("|c59E08AVerified|r")
                    row.col4:SetText(string.format("|c00FFCC%s|r", item.guildName or "None"))
                    row.col5:SetText("|c888888Logged|r")

                    local timeAgo = item.timestamp and ZO_FormatDurationAgo(GetTimeStamp() - item.timestamp) or "--"
                    row.col6:SetText(timeAgo)
                end
            else
                row:SetHidden(true)
                row.bidData = nil
            end
        end
    end

    if self.bidsPageLbl then
        self.bidsPageLbl:SetText(string.format("Page %d/%d (%d records)", self.bidsCurrentPage, maxPages, total))
    end

    if self.bidsStatLbl then
        if isBids then
            self.bidsStatLbl:SetText(string.format("Bids Ledger: %d records  •  Scroll wheel to page", total))
        else
            self.bidsStatLbl:SetText(string.format("Ground Recon: %d trader kiosks cataloged in Tamriel", total))
        end
    end
end
