--[[
    FissalRelay_Bids.lua
    Kiosk Recon & Bids Vault for Fissal Relay Prime
    Crafted by Echo & Fissal for Fissal Relay and the Redfur Guilds.

    Features:
      • Comprehensive Kiosk Bids Ledger tracking all guild kiosk bid submissions,
        direct purchases, and refunds parsed from LibHistoire banked currency events
      • Ground Recon Vault cataloging scouted trader kiosks across Tamriel
      • Real-time status badges (Won, Refunded, Pending) with gold totals
      • Manual "Scout Current Kiosk" action for immediate field logging
]]--

FissalRelay = FissalRelay or {}
local FR = FissalRelay

local ROWS_PER_PAGE = 9

FR.bidsSubView = "bids" -- "bids" or "recon"
FR.bidsCurrentPage = 1
FR.bidsList = {}

--[[ =========================================================================
     DATA GATHERING
========================================================================= ]]--

function FR:CollectBidsData()
    local bids = {}
    if self.savedVars and self.savedVars.staff and self.savedVars.staff.bids then
        for id, b in pairs(self.savedVars.staff.bids) do
            table.insert(bids, b)
        end
    end
    table.sort(bids, function(a, b)
        return (a.timestamp or 0) > (b.timestamp or 0)
    end)
    self.bidsList = bids
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

    -- 1. Backdrop Card
    local card = wm:CreateControl("$(parent)_Card", panel, CT_BACKDROP)
    card:SetAnchorFill()
    card:SetCenterColor(0.06, 0.06, 0.08, 0.85)
    card:SetEdgeColor(0.30, 0.25, 0.18, 0.70)
    card:SetEdgeTexture("", 8, 1, 0)

    -- 2. Sub-View Switcher Bar
    local bidsBtn = wm:CreateControl("$(parent)_BidsBtn", card, CT_BUTTON)
    bidsBtn:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 8)
    bidsBtn:SetDimensions(150, 24)
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
        tooltipText = "View all recorded guild kiosk bid submissions, won kiosks, and bid refunds from LibHistoire banked currency logs.",
    })
    bidsBtn:SetHandler("OnClicked", function()
        self.bidsSubView = "bids"
        self.bidsCurrentPage = 1
        self:UpdateBidsUI()
    end)
    self.bidsTabBtn = bidsBtn

    local reconBtn = wm:CreateControl("$(parent)_ReconBtn", card, CT_BUTTON)
    reconBtn:SetAnchor(LEFT, bidsBtn, RIGHT, 8, 0)
    reconBtn:SetDimensions(150, 24)
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
    scoutNowBtn:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 8)
    scoutNowBtn:SetDimensions(160, 24)
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

    -- 3. Column Header
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
    h1:SetText("|cFF9900GUILD / LOCATION|r")
    self.bidsH1 = h1

    local h2 = wm:CreateControl("$(parent)_H2", colHeader, CT_LABEL)
    h2:SetAnchor(LEFT, colHeader, LEFT, 220, 0)
    h2:SetFont("ZoFontGameBold")
    h2:SetText("|c00FFCCKIOSK TRADER|r")
    self.bidsH2 = h2

    local h3 = wm:CreateControl("$(parent)_H3", colHeader, CT_LABEL)
    h3:SetAnchor(LEFT, colHeader, LEFT, 380, 0)
    h3:SetFont("ZoFontGameBold")
    h3:SetText("|cFFD700AMOUNT|r")
    self.bidsH3 = h3

    local h4 = wm:CreateControl("$(parent)_H4", colHeader, CT_LABEL)
    h4:SetAnchor(LEFT, colHeader, LEFT, 480, 0)
    h4:SetFont("ZoFontGameBold")
    h4:SetText("|c59E08ASTATUS / BIDDER|r")
    self.bidsH4 = h4

    local h5 = wm:CreateControl("$(parent)_H5", colHeader, CT_LABEL)
    h5:SetAnchor(RIGHT, colHeader, RIGHT, -14, 0)
    h5:SetFont("ZoFontGameBold")
    h5:SetText("|c888888DATE / TIME|r")
    self.bidsH5 = h5

    -- 4. Table Rows (9 Rows)
    self.bidsRows = {}
    local rowY = headerY + 24

    for r = 1, ROWS_PER_PAGE do
        local row = wm:CreateControl("$(parent)_Row_" .. r, card, CT_BACKDROP)
        row:SetAnchor(TOPLEFT, card, TOPLEFT, 10, rowY + (r - 1) * 35)
        row:SetAnchor(TOPRIGHT, card, TOPRIGHT, -10, rowY + (r - 1) * 35)
        row:SetHeight(33)
        row:SetCenterColor(0.05, 0.05, 0.07, 0.70)
        row:SetEdgeColor(0.20, 0.18, 0.14, 0.40)
        row:SetEdgeTexture("", 8, 1, 0)

        local col1 = wm:CreateControl("$(parent)_Col1", row, CT_LABEL)
        col1:SetAnchor(LEFT, row, LEFT, 8, 0)
        col1:SetDimensions(205, 20)
        col1:SetFont("ZoFontGameMedium")
        col1:SetText("--")
        row.col1 = col1

        local col2 = wm:CreateControl("$(parent)_Col2", row, CT_LABEL)
        col2:SetAnchor(LEFT, row, LEFT, 220, 0)
        col2:SetDimensions(155, 20)
        col2:SetFont("ZoFontGameSmall")
        col2:SetText("--")
        row.col2 = col2

        local col3 = wm:CreateControl("$(parent)_Col3", row, CT_LABEL)
        col3:SetAnchor(LEFT, row, LEFT, 380, 0)
        col3:SetDimensions(95, 20)
        col3:SetFont("ZoFontGameBold")
        col3:SetText("--")
        row.col3 = col3

        local col4 = wm:CreateControl("$(parent)_Col4", row, CT_LABEL)
        col4:SetAnchor(LEFT, row, LEFT, 480, 0)
        col4:SetDimensions(115, 20)
        col4:SetFont("ZoFontGameSmall")
        col4:SetText("--")
        row.col4 = col4

        local col5 = wm:CreateControl("$(parent)_Col5", row, CT_LABEL)
        col5:SetAnchor(RIGHT, row, RIGHT, -14, 0)
        col5:SetFont("ZoFontGameSmall")
        col5:SetText("--")
        row.col5 = col5

        self.bidsRows[r] = row
    end

    -- 5. Footer & Pagination
    local footerY = -8
    local statLbl = wm:CreateControl("$(parent)_StatLbl", card, CT_LABEL)
    statLbl:SetAnchor(BOTTOMLEFT, card, BOTTOMLEFT, 12, footerY)
    statLbl:SetFont("ZoFontGameSmall")
    statLbl:SetText("Vault: Loading...")
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
     RENDERING & LOGIC
========================================================================= ]]--

function FR:UpdateBidsUI()
    self:CollectBidsData()
    self:CollectReconData()

    local isBids = (self.bidsSubView == "bids")

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

    if isBids then
        if self.bidsH1 then self.bidsH1:SetText("|cFF9900GUILD|r") end
        if self.bidsH2 then self.bidsH2:SetText("|c00FFCCKIOSK TRADER|r") end
        if self.bidsH3 then self.bidsH3:SetText("|cFFD700AMOUNT|r") end
        if self.bidsH4 then self.bidsH4:SetText("|c59E08ASTATUS / BIDDER|r") end
        if self.bidsH5 then self.bidsH5:SetText("|c888888DATE / TIME|r") end
    else
        if self.bidsH1 then self.bidsH1:SetText("|cFF9900TRADER NPC|r") end
        if self.bidsH2 then self.bidsH2:SetText("|c00FFCCCITY / ZONE|r") end
        if self.bidsH3 then self.bidsH3:SetText("|cFFD700STATUS|r") end
        if self.bidsH4 then self.bidsH4:SetText("|c59E08ACONTROLLING GUILD|r") end
        if self.bidsH5 then self.bidsH5:SetText("|c888888LAST SCOUTED|r") end
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
            if itemIndex <= total then
                local item = list[itemIndex]
                row:SetHidden(false)

                if isBids then
                    -- Bids item
                    row.col1:SetText(string.format("|c00FFCC%s|r", item.guildName or "Guild"))
                    row.col2:SetText(item.kioskName or "Unknown Kiosk")
                    row.col3:SetText(string.format("|cFFD700%sg|r", ZO_LocalizeDecimalNumber(item.amount or 0)))

                    local statusColor = (item.status == "Won" or item.status == "Direct Purchase (Won)") and "59E08A"
                        or (item.status == "Refunded (Lost)" and "FF5555" or "FFCC00")
                    row.col4:SetText(string.format("|c%s%s|r", statusColor, item.status or "Pending"))

                    local timeAgo = item.timestamp and ZO_FormatDurationAgo(GetTimeStamp() - item.timestamp) or "--"
                    row.col5:SetText(timeAgo)
                else
                    -- Recon item
                    row.col1:SetText(string.format("|cFFFFFF%s|r", item.trader or "Trader"))
                    row.col2:SetText(string.format("%s, %s", item.city or "?", item.zone or "?"))
                    row.col3:SetText("|c59E08AVerified|r")
                    row.col4:SetText(string.format("|c00FFCC%s|r", item.guildName or "None"))

                    local timeAgo = item.timestamp and ZO_FormatDurationAgo(GetTimeStamp() - item.timestamp) or "--"
                    row.col5:SetText(timeAgo)
                end
            else
                row:SetHidden(true)
            end
        end
    end

    if self.bidsPageLbl then
        self.bidsPageLbl:SetText(string.format("Page %d/%d (%d records)", self.bidsCurrentPage, maxPages, total))
    end

    if self.bidsStatLbl then
        if isBids then
            self.bidsStatLbl:SetText(string.format("Bids Ledger: %d records captured via LibHistoire", total))
        else
            self.bidsStatLbl:SetText(string.format("Ground Recon: %d trader kiosks cataloged in Tamriel", total))
        end
    end
end
