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
FR.reconStatusFilter = "ALL" -- "ALL", "VERIFIED", "UNVERIFIED", "VACANT", "OUTLAW"
FR.reconSearchQuery = ""
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
    local reconMap = {}
    local curWeekStart = self.GetTuesdayTradeWeek and self:GetTuesdayTradeWeek(GetTimeStamp()) or 0

    local function IsKioskVacant(k)
        if k.guildId and tonumber(k.guildId) and tonumber(k.guildId) > 0 then return false end
        local src = tostring(k.source or "")
        if src == "Guild Finder Sweep" or src == "Guild ID Probe" or src == "Owned Guild Stall" or src == "Owned Ledger" then
            return false
        end
        if k.isOpen == true or k.isVacant == true then return true end
        local gName = string.lower(k.guildName or "")
        if gName == "" or gName == "none" or gName == "empty" or gName == "open" or gName == "vacant" or gName == "unhired" or gName == "no trader" or gName == "open spot" or gName == "none (open spot)" or gName == "no hiring guild (vacant)" or gName == "unclaimed" then
            return true
        end
        return false
    end

    -- 0. Seed all 291 canonical Tamriel kiosks from KIOSK_DIRECTORY
    if self.KIOSK_DIRECTORY then
        for traderName, dirInfo in pairs(self.KIOSK_DIRECTORY) do
            local traderKey = string.lower(traderName)
            reconMap[traderKey] = {
                trader = dirInfo.trader or traderName,
                city = dirInfo.city or "Tamriel",
                zone = dirInfo.zone or "Tamriel",
                guildId = 0,
                guildName = "Unverified",
                timestamp = 0,
                isOpen = false,
                isCurrentWeek = false,
                isPriorWeek = false,
                isUnverified = true,
                source = "Directory",
                notes = dirInfo.isOutlaw and "Outlaws Refuge" or "",
            }
        end
    end

    -- 1. Ingest synced spreadsheet kiosks from Fissal Relay
    local sheetKiosks = (self.savedVars and self.savedVars.spreadsheetKiosks) or self.SpreadsheetKiosks or {}
    for key, sk in pairs(sheetKiosks) do
        local rawName = sk.trader or key
        local resolved = self.ResolveCanonicalKiosk and self:ResolveCanonicalKiosk(rawName, sk.city, sk.zone) or nil
        if not resolved or resolved.matchedDirectory then
            local canonicalName = (resolved and resolved.trader) or rawName
            local traderKey = string.lower(canonicalName)
            local isOpen = IsKioskVacant(sk)
            local ts = tonumber(sk.timestamp or sk.updatedAt) or 0
            local isCur = (ts >= curWeekStart and curWeekStart > 0)
            reconMap[traderKey] = {
                trader = canonicalName,
                city = (resolved and resolved.city) or sk.city or "Unknown",
                zone = (resolved and resolved.zone) or sk.zone or "Unknown",
                guildId = tonumber(sk.guildId) or 0,
                guildName = isOpen and "None (Open Spot)" or (sk.guildName or "Unknown"),
                timestamp = ts,
                isOpen = isOpen,
                isCurrentWeek = isCur,
                isPriorWeek = (ts > 0 and not isCur),
                isUnverified = (ts == 0),
                source = "Spreadsheet",
                notes = sk.notes or "",
            }
        end
    end

    -- 2. Ingest in-game ground truth & Guild Finder sweep observations (authoritative recon)
    if self.savedVars and self.savedVars.kiosks then
        for trader, k in pairs(self.savedVars.kiosks) do
            local rawName = k.trader or trader
            local resolved = self.ResolveCanonicalKiosk and self:ResolveCanonicalKiosk(rawName, k.city, k.zone) or nil
            if not self.ResolveCanonicalKiosk or (resolved and resolved.matchedDirectory and not self:IsBlacklistedTrader(resolved.trader)) then
                local canonicalName = (resolved and resolved.trader) or rawName
                local traderKey = string.lower(canonicalName)
                local isOpen = IsKioskVacant(k)
                local existing = reconMap[traderKey]
                local ts = tonumber(k.timestamp) or 0
                if not existing or ts >= (existing.timestamp or 0) then
                    local isCur = (ts >= curWeekStart and curWeekStart > 0)
                    local effGuildName = k.guildName or "None"
                    if not isOpen and string.lower(effGuildName) == "vacant" then
                        effGuildName = "Available Trader Kiosk"
                    end
                    reconMap[traderKey] = {
                        trader = canonicalName,
                        city = (resolved and resolved.city) or k.city or (existing and existing.city) or "?",
                        zone = (resolved and resolved.zone) or k.zone or (existing and existing.zone) or "?",
                        guildId = tonumber(k.guildId) or 0,
                        guildName = isOpen and "None (Open Spot)" or effGuildName,
                        timestamp = ts,
                        isOpen = isOpen,
                        isCurrentWeek = isCur,
                        isPriorWeek = (ts > 0 and not isCur),
                        isUnverified = (ts == 0),
                        source = k.source or "Scouted",
                        notes = k.notes or (existing and existing.notes) or "",
                    }
                end
            end
        end
    end

    local recon = {}
    local verifiedThisWeekCount = 0
    local vacantCount = 0
    local unverifiedCount = 0
    local totalDirectoryCount = 0
    local statusFilt = self.reconStatusFilter or "ALL"
    local searchQ = string.lower(tostring(self.reconSearchQuery or "")):gsub("^%s+", ""):gsub("%s+$", "")

    for _, k in pairs(reconMap) do
        totalDirectoryCount = totalDirectoryCount + 1
        if k.isCurrentWeek then
            verifiedThisWeekCount = verifiedThisWeekCount + 1
            if k.isOpen then
                vacantCount = vacantCount + 1
            end
        elseif k.isUnverified then
            unverifiedCount = unverifiedCount + 1
        end

        local passStatus = true
        if statusFilt == "VERIFIED" then
            passStatus = (k.isCurrentWeek == true)
        elseif statusFilt == "UNVERIFIED" then
            passStatus = (not k.isCurrentWeek)
        elseif statusFilt == "VACANT" then
            passStatus = (k.isOpen == true and k.isCurrentWeek == true)
        elseif statusFilt == "OUTLAW" then
            passStatus = (string.find(string.lower(k.notes or ""), "outlaw") ~= nil) or (string.find(string.lower(k.city or ""), "refuge") ~= nil)
        end

        local passSearch = true
        if searchQ ~= "" then
            local hay = string.lower(string.format("%s %s %s %s %s %s",
                k.trader or "",
                k.city or "",
                k.zone or "",
                k.guildName or "",
                k.source or "",
                k.notes or ""))
            passSearch = (string.find(hay, searchQ, 1, true) ~= nil)
        end

        if passStatus and passSearch then
            table.insert(recon, k)
        end
    end
    self.reconTotalDirectoryCount = totalDirectoryCount > 0 and totalDirectoryCount or 291
    self.reconVerifiedThisWeekCount = verifiedThisWeekCount
    self.reconVacantCount = vacantCount
    self.reconUnverifiedCount = unverifiedCount

    local sortCol = self.reconSortCol
    local asc = self.reconSortAsc or false

    local function DefaultPriorityCompare(a, b)
        local aOpenCur = (a.isOpen and a.isCurrentWeek) and 1 or 0
        local bOpenCur = (b.isOpen and b.isCurrentWeek) and 1 or 0
        if aOpenCur ~= bOpenCur then
            return aOpenCur > bOpenCur
        end
        local aCur = a.isCurrentWeek and 1 or 0
        local bCur = b.isCurrentWeek and 1 or 0
        if aCur ~= bCur then
            return aCur > bCur
        end
        if (a.timestamp or 0) ~= (b.timestamp or 0) then
            return (a.timestamp or 0) > (b.timestamp or 0)
        end
        if (a.zone or "") ~= (b.zone or "") then
            return (a.zone or "") < (b.zone or "")
        end
        return (a.trader or "") < (b.trader or "")
    end

    local function ReconStatusTier(item)
        if item.isOpen and item.isCurrentWeek then return 4 end
        if item.isCurrentWeek then return 3 end
        if item.isPriorWeek then return 2 end
        return 1
    end

    -- 3. Sort by selected Recon column header or default 4-tier priority
    table.sort(recon, function(a, b)
        if not sortCol then
            return DefaultPriorityCompare(a, b)
        end
        local valA, valB
        if sortCol == "guild" then -- TRADER NPC
            valA = string.lower(a.trader or "")
            valB = string.lower(b.trader or "")
        elseif sortCol == "kiosk" then -- CITY / ZONE
            valA = string.lower((a.city or "") .. " " .. (a.zone or ""))
            valB = string.lower((b.city or "") .. " " .. (b.zone or ""))
        elseif sortCol == "bidder" then -- STATUS
            valA = ReconStatusTier(a)
            valB = ReconStatusTier(b)
        elseif sortCol == "amount" then -- CONTROLLING GUILD
            valA = string.lower(a.guildName or "")
            valB = string.lower(b.guildName or "")
        elseif sortCol == "status" then -- SOURCE
            valA = string.lower(a.source or "")
            valB = string.lower(b.source or "")
        else -- "timestamp" / SCOUTED
            valA = tonumber(a.timestamp) or 0
            valB = tonumber(b.timestamp) or 0
        end

        if valA == valB then
            return DefaultPriorityCompare(a, b)
        end
        if asc then
            return valA < valB
        else
            return valA > valB
        end
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
        tooltipText = "View all trader kiosks scouted in-person or harvested via Guild Finder across Tamriel.",
    })
    reconBtn:SetHandler("OnClicked", function()
        self.bidsSubView = "recon"
        self.bidsCurrentPage = 1
        self:UpdateBidsUI()
    end)
    self.reconTabBtn = reconBtn

    local scoutNowBtn = wm:CreateControl("$(parent)_ScoutNowBtn", card, CT_BUTTON)
    scoutNowBtn:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 6)
    scoutNowBtn:SetDimensions(145, 24)
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
        tooltipText = "Record an instant reconnaissance log of the guild trader you are currently interacting with or targeting in the world.",
    })
    scoutNowBtn:SetHandler("OnClicked", function()
        local recorded = self:RecordKioskObservation()
        if not recorded and self.InspectUnitKiosk then
            recorded = self:InspectUnitKiosk("interact", "Manual Scout", false)
                or self:InspectUnitKiosk("reticleover", "Manual Scout", false)
        end
        if recorded then
            self.PrintChat("Ground recon observation captured!")
            self:UpdateBidsUI()
        else
            self.PrintChat("No trader interaction or reticle target active. Aim at or open a guild trader to scout.")
        end
    end)

    local sweepTamrielBtn = wm:CreateControl("$(parent)_SweepTamrielBtn", card, CT_BUTTON)
    sweepTamrielBtn:SetAnchor(RIGHT, scoutNowBtn, LEFT, -8, 0)
    sweepTamrielBtn:SetDimensions(155, 24)
    sweepTamrielBtn:SetFont("ZoFontGameSmall")
    sweepTamrielBtn:SetText("Sweep Tamriel Kiosks")
    self:StyleTactileButton(sweepTamrielBtn, {
        normalBg = { 0.14, 0.10, 0.04, 0.90 },
        hoverBg = { 0.22, 0.16, 0.06, 0.98 },
        normalEdge = { 0.85, 0.65, 0.15, 0.80 },
        hoverEdge = { 1.0, 0.85, 0.25, 1.0 },
        normalTextColor = { 1.0, 0.85, 0.30, 1 },
        hoverTextColor = { 1.0, 0.95, 0.60, 1 },
        tooltipTitle = "Autonomous Tamriel Kiosk Sweep",
        tooltipText = "Sweeps all Guild Finder categories, alliances, and languages + probes 630+ known PC-NA trading guild IDs to map Tamriel guild traders without porting.",
    })
    sweepTamrielBtn:SetHandler("OnClicked", function()
        if self.StartKioskSweep then
            self:StartKioskSweep()
        else
            self.PrintChat("Kiosk Recon Sweep engine not loaded.")
        end
    end)
    self.sweepTamrielBtn = sweepTamrielBtn

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
    allGuildBtn:SetDimensions(74, 22)
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
    for g = 1, math.min(numGuilds, 5) do
        local gId = GetGuildId(g)
        local gName = GetGuildName(gId)
        local shortName = gName
        if string.find(string.lower(gName), "post") then shortName = "Post"
        elseif string.find(string.lower(gName), "caravan") then shortName = "Caravan"
        elseif string.find(string.lower(gName), "dealer") then shortName = "Dealers"
        else
            shortName = string.sub(gName, 1, 9)
        end

        local gBtn = wm:CreateControl("$(parent)_G_" .. gId, filterBar, CT_BUTTON)
        gBtn:SetAnchor(LEFT, prevCtrl, RIGHT, 6, 0)
        gBtn:SetDimensions(72, 22)
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
    allRoundsBtn:SetDimensions(82, 22)
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
    priorRoundBtn:SetDimensions(90, 22)
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
    activeRoundBtn:SetDimensions(102, 22)
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

    -- 3B. Secondary Filter Bar for Ground Recon Vault (Status Filter Pills & Live Search Box)
    local reconFilterBar = wm:CreateControl("$(parent)_ReconFilterBar", card, CT_CONTROL)
    reconFilterBar:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 34)
    reconFilterBar:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 34)
    reconFilterBar:SetHeight(28)
    reconFilterBar:SetHidden(true)
    self.reconFilterBar = reconFilterBar

    self.reconStatusBtns = {}
    local reconFilters = {
        { id = "ALL", label = "All (291)", width = 76, tip = "Show all 291 canonical Tamriel guild traders." },
        { id = "VERIFIED", label = "Verified", width = 72, tip = "Show only kiosks verified for the current Tuesday trade week." },
        { id = "UNVERIFIED", label = "Unverified", width = 82, tip = "Show only kiosks not yet verified this trade week (ideal for targeted scouting!)." },
        { id = "VACANT", label = "Vacant ⭐", width = 78, tip = "Show only verified Vacant / Open Spot kiosks available for immediate hire." },
        { id = "OUTLAW", label = "Refuges", width = 68, tip = "Show only Outlaws Refuge traders across Tamriel." },
    }
    local prevReconBtn = nil
    for _, rf in ipairs(reconFilters) do
        local rBtn = wm:CreateControl("$(parent)_RF_" .. rf.id, reconFilterBar, CT_BUTTON)
        if not prevReconBtn then
            rBtn:SetAnchor(LEFT, reconFilterBar, LEFT, 0, 0)
        else
            rBtn:SetAnchor(LEFT, prevReconBtn, RIGHT, 6, 0)
        end
        rBtn:SetDimensions(rf.width, 22)
        rBtn:SetFont("ZoFontGameSmall")
        rBtn:SetText(rf.label)
        self:StyleTactileButton(rBtn, {
            normalBg = { 0.08, 0.08, 0.12, 0.85 },
            hoverBg = { 0.08, 0.18, 0.20, 0.95 },
            normalEdge = { 0.30, 0.30, 0.35, 0.65 },
            hoverEdge = { 0, 0.90, 0.80, 1.0 },
            tooltipTitle = rf.label,
            tooltipText = rf.tip,
        })
        rBtn:SetHandler("OnClicked", function()
            self.reconStatusFilter = rf.id
            self.bidsCurrentPage = 1
            self:UpdateBidsFilterButtons()
            self:UpdateBidsUI()
        end)
        self.reconStatusBtns[rf.id] = rBtn
        prevReconBtn = rBtn
    end

    -- Clear Search Button [×] on far right of reconFilterBar
    local clearSearchBtn = wm:CreateControl("$(parent)_ClearSearch", reconFilterBar, CT_BUTTON)
    clearSearchBtn:SetAnchor(RIGHT, reconFilterBar, RIGHT, 0, 0)
    clearSearchBtn:SetDimensions(24, 22)
    clearSearchBtn:SetFont("ZoFontGameBold")
    clearSearchBtn:SetText("×")
    self:StyleTactileButton(clearSearchBtn, {
        normalBg = { 0.12, 0.06, 0.06, 0.85 },
        hoverBg = { 0.24, 0.08, 0.08, 0.95 },
        normalEdge = { 0.45, 0.22, 0.22, 0.70 },
        hoverEdge = { 1.0, 0.35, 0.35, 1.0 },
        tooltipTitle = "Clear Search",
        tooltipText = "Clear the trader/city/zone/guild search filter.",
    })

    local searchBg = wm:CreateControl("$(parent)_SearchBg", reconFilterBar, CT_BACKDROP)
    searchBg:SetAnchor(RIGHT, clearSearchBtn, LEFT, -4, 0)
    searchBg:SetDimensions(235, 22)
    searchBg:SetCenterColor(0.03, 0.04, 0.06, 0.92)
    searchBg:SetEdgeColor(0, 0.75, 0.65, 0.65)
    searchBg:SetEdgeTexture("", 8, 1, 0)

    local searchIconLbl = wm:CreateControl("$(parent)_SearchHint", searchBg, CT_LABEL)
    searchIconLbl:SetAnchor(LEFT, searchBg, LEFT, 6, 0)
    searchIconLbl:SetFont("ZoFontGameSmall")
    searchIconLbl:SetColor(0.45, 0.65, 0.65, 0.85)
    searchIconLbl:SetText("Filter:")

    local searchEdit = wm:CreateControl("$(parent)_SearchEdit", searchBg, CT_EDITBOX)
    searchEdit:SetAnchor(LEFT, searchIconLbl, RIGHT, 5, 0)
    searchEdit:SetAnchor(RIGHT, searchBg, RIGHT, -6, 0)
    searchEdit:SetHeight(20)
    searchEdit:SetFont("ZoFontGameSmall")
    searchEdit:SetMaxInputChars(48)
    searchEdit:SetMouseEnabled(true)
    searchEdit:SetHandler("OnTextChanged", function(eb)
        local txt = eb:GetText() or ""
        if self.reconSearchQuery ~= txt then
            self.reconSearchQuery = txt
            self.bidsCurrentPage = 1
            self:CollectReconData()
            self:RenderBidsRows()
        end
    end)
    searchEdit:SetHandler("OnEscape", function(eb)
        eb:LoseFocus()
    end)
    searchEdit:SetHandler("OnEnter", function(eb)
        eb:LoseFocus()
    end)
    self.reconSearchEdit = searchEdit

    clearSearchBtn:SetHandler("OnClicked", function()
        self.reconSearchQuery = ""
        if self.reconSearchEdit then
            self.reconSearchEdit:SetText("")
            self.reconSearchEdit:LoseFocus()
        end
        self.bidsCurrentPage = 1
        self:UpdateBidsUI()
    end)

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
            if self.bidsSubView == "recon" then
                if self.reconSortCol == sortKey then
                    self.reconSortAsc = not self.reconSortAsc
                else
                    self.reconSortCol = sortKey
                    self.reconSortAsc = true
                end
                self:UpdateHeaderArrows()
                self:CollectReconData()
                self:RenderBidsRows()
            else
                if self.bidsSortCol == sortKey then
                    self.bidsSortAsc = not self.bidsSortAsc
                else
                    self.bidsSortCol = sortKey
                    self.bidsSortAsc = false
                end
                self:UpdateHeaderArrows()
                self:CollectBidsData()
                self:RenderBidsRows()
            end
        end)
        return btn
    end

    self.bidsH1 = MakeSortHeader("H1", colHeader, LEFT, LEFT, 8, 140, "GUILD ▲", "guild")
    self.bidsH2 = MakeSortHeader("H2", self.bidsH1, LEFT, RIGHT, 8, 240, "KIOSK TRADER & LOCATION", "kiosk")
    self.bidsH3 = MakeSortHeader("H3", self.bidsH2, LEFT, RIGHT, 8, 120, "BIDDER", "bidder")
    self.bidsH4 = MakeSortHeader("H4", self.bidsH3, LEFT, RIGHT, 8, 155, "AMOUNT", "amount")
    self.bidsH5 = MakeSortHeader("H5", self.bidsH4, LEFT, RIGHT, 8, 125, "STATUS", "status")
    self.bidsH6 = MakeSortHeader("H6", self.bidsH5, LEFT, RIGHT, 8, 88, "DATE / AGE", "timestamp")

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
        col1:SetDimensions(140, 20)
        col1:SetFont("ZoFontGameMedium")
        col1:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        col1:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        row.col1 = col1

        local col2 = wm:CreateControl("$(parent)_Col2", row, CT_LABEL)
        col2:SetAnchor(LEFT, col1, RIGHT, 8, 0)
        col2:SetDimensions(240, 20)
        col2:SetFont("ZoFontGameSmall")
        col2:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        col2:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        row.col2 = col2

        local col3 = wm:CreateControl("$(parent)_Col3", row, CT_LABEL)
        col3:SetAnchor(LEFT, col2, RIGHT, 8, 0)
        col3:SetDimensions(120, 20)
        col3:SetFont("ZoFontGameSmall")
        col3:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        col3:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        row.col3 = col3

        local col4 = wm:CreateControl("$(parent)_Col4", row, CT_LABEL)
        col4:SetAnchor(LEFT, col3, RIGHT, 8, 0)
        col4:SetDimensions(155, 20)
        col4:SetFont("ZoFontGameBold")
        col4:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        col4:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        row.col4 = col4

        local col5 = wm:CreateControl("$(parent)_Col5", row, CT_LABEL)
        col5:SetAnchor(LEFT, col4, RIGHT, 8, 0)
        col5:SetDimensions(125, 20)
        col5:SetFont("ZoFontGameSmall")
        col5:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        col5:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        row.col5 = col5

        local col6 = wm:CreateControl("$(parent)_Col6", row, CT_LABEL)
        col6:SetAnchor(LEFT, col5, RIGHT, 8, 0)
        col6:SetAnchor(RIGHT, row, RIGHT, -8, 0)
        col6:SetFont("ZoFontGameSmall")
        col6:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        col6:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
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
            elseif control.reconData then
                local rData = control.reconData
                InitializeTooltip(InformationTooltip, control, RIGHT, 5, 0)
                InformationTooltip:AddLine(string.format("|c00FFCC%s|r", rData.trader or "Trader"), "ZoFontGameBold")
                InformationTooltip:AddLine(string.format("• Location: |cFFFFFF%s, %s|r", rData.city or "?", rData.zone or "?"), "ZoFontGameSmall")
                InformationTooltip:AddLine(string.format("• Controlling Guild: |cFFD700%s|r", rData.guildName or "Unverified"), "ZoFontGameSmall")
                InformationTooltip:AddLine(string.format("• Source: |c59E08A%s|r", rData.source or "Directory"), "ZoFontGameSmall")
                if rData.notes and rData.notes ~= "" then
                    InformationTooltip:AddLine(string.format("• Notes: |cCCCCCC%s|r", rData.notes), "ZoFontGameSmall")
                end
                if rData.timestamp and rData.timestamp > 0 then
                    local dateStr = os.date("%A, %b %d at %H:%M UTC", rData.timestamp)
                    InformationTooltip:AddLine(string.format("• Scouted: |c888888%s|r", dateStr), "ZoFontGameSmall")
                else
                    InformationTooltip:AddLine("• Scouted: |c888888Unverified this week|r", "ZoFontGameSmall")
                end
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

    -- Recon Status Buttons
    for id, btn in pairs(self.reconStatusBtns or {}) do
        if btn.bg then
            if id == (self.reconStatusFilter or "ALL") then
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
    if self.bidsSubView == "recon" then
        local arrow = self.reconSortAsc and " ▲" or " ▼"
        local col = self.reconSortCol
        if self.bidsH1 then self.bidsH1:SetText("TRADER NPC" .. (col == "guild" and arrow or "")) end
        if self.bidsH2 then self.bidsH2:SetText("CITY / ZONE" .. (col == "kiosk" and arrow or "")) end
        if self.bidsH3 then self.bidsH3:SetText("STATUS" .. (col == "bidder" and arrow or "")) end
        if self.bidsH4 then self.bidsH4:SetText("CONTROLLING GUILD" .. (col == "amount" and arrow or "")) end
        if self.bidsH5 then self.bidsH5:SetText("SOURCE" .. (col == "status" and arrow or "")) end
        if self.bidsH6 then self.bidsH6:SetText("SCOUTED" .. (col == "timestamp" and arrow or "")) end
    else
        local arrow = self.bidsSortAsc and " ▲" or " ▼"
        local col = self.bidsSortCol
        if self.bidsH1 then self.bidsH1:SetText("GUILD" .. (col == "guild" and arrow or "")) end
        if self.bidsH2 then self.bidsH2:SetText("KIOSK TRADER & LOCATION" .. (col == "kiosk" and arrow or "")) end
        if self.bidsH3 then self.bidsH3:SetText("BIDDER" .. (col == "bidder" and arrow or "")) end
        if self.bidsH4 then self.bidsH4:SetText("AMOUNT" .. (col == "amount" and arrow or "")) end
        if self.bidsH5 then self.bidsH5:SetText("STATUS" .. (col == "status" and arrow or "")) end
        if self.bidsH6 then self.bidsH6:SetText("DATE" .. (col == "timestamp" and arrow or "")) end
    end
end

--[[ =========================================================================
     RENDERING & LOGIC
========================================================================= ]]--

function FR:UpdateBidsUI()
    local isBids = (self.bidsSubView == "bids")
    if isBids then
        self:CollectBidsData()
    else
        self:CollectReconData()
    end
    self:UpdateBidsFilterButtons()
    self:UpdateHeaderArrows()

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

    -- Toggle filter bar and keep ribbon visible in both views
    if self.bidsFilterBar then self.bidsFilterBar:SetHidden(not isBids) end
    if self.reconFilterBar then self.reconFilterBar:SetHidden(isBids) end
    if self.bidsRibbon then self.bidsRibbon:SetHidden(false) end

    -- Update column header labels
    self:UpdateHeaderArrows()

    -- Financial & Recon Telemetry calculation
    if self.bidsRibbonLbl then
        if isBids then
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
        else
            local verifiedWk = self.reconVerifiedThisWeekCount or 0
            local vacantCnt = self.reconVacantCount or 0
            local unverifiedCnt = self.reconUnverifiedCount or 0
            local totalDir = self.reconTotalDirectoryCount or 291
            self.bidsRibbonLbl:SetText(string.format(
                "• |c00FFCCGround Recon:|r Verified This Week: |c59E08A%d / %d|r  |  Vacant: |cFFD700%d|r  |  Unverified: |c888888%d|r  |  Showing: |cFFFFFF%d|r",
                verifiedWk, totalDir, vacantCnt, unverifiedCnt, #(self.reconList or {})
            ))
        end
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
                row.reconData = (not isBids) and item or nil

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
                    local timeAgo = (item.timestamp and item.timestamp > 0) and ZO_FormatDurationAgo(GetTimeStamp() - item.timestamp) or "--"
                    if item.isOpen and item.isCurrentWeek then
                        row:SetCenterColor(0.06, 0.16, 0.08, 0.75)
                        row.col1:SetText(string.format("|cFFD700⭐ %s|r", item.trader or "Trader"))
                        row.col2:SetText(string.format("%s, %s", item.city or "?", item.zone or "?"))
                        row.col3:SetText("|c59E08A⭐ VACANT / OPEN|r")
                        row.col4:SetText("|c00FFCC(No Guild Claimed)|r")
                        row.col5:SetText(string.format("|c00FFCC%s|r", item.source or "Recon"))
                        row.col6:SetText(timeAgo)
                    elseif item.isCurrentWeek then
                        row.col1:SetText(string.format("|cFFFFFF%s|r", item.trader or "Trader"))
                        row.col2:SetText(string.format("%s, %s", item.city or "?", item.zone or "?"))
                        row.col3:SetText("|c59E08A● Verified (Week)|r")
                        row.col4:SetText(string.format("|c00FFCC%s|r", item.guildName or "None"))
                        row.col5:SetText(string.format("|c59E08A%s|r", item.source or "Scouted"))
                        row.col6:SetText(timeAgo)
                    elseif item.isPriorWeek then
                        row.col1:SetText(string.format("|cCCCCCC%s|r", item.trader or "Trader"))
                        row.col2:SetText(string.format("%s, %s", item.city or "?", item.zone or "?"))
                        row.col3:SetText("|cE6C387○ Prior Week|r")
                        row.col4:SetText(string.format("|c888888%s|r", item.guildName or "None"))
                        row.col5:SetText(string.format("|c888888%s|r", item.source or "Prior"))
                        row.col6:SetText(timeAgo)
                    else
                        row.col1:SetText(string.format("|c777777%s|r", item.trader or "Trader"))
                        row.col2:SetText(string.format("|c777777%s, %s|r", item.city or "?", item.zone or "?"))
                        row.col3:SetText("|c666666○ Unscavenged|r")
                        row.col4:SetText("|c555555Unverified|r")
                        row.col5:SetText("|c555555Directory|r")
                        row.col6:SetText("--")
                    end
                end
            else
                row:SetHidden(true)
                row.bidData = nil
                row.reconData = nil
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
            local verifiedWk = self.reconVerifiedThisWeekCount or 0
            local knownGuilds = NonContiguousCount((self.savedVars and self.savedVars.knownTradingGuilds) or {})
            self.bidsStatLbl:SetText(string.format("Ground Recon: %d/%d Tamriel kiosks verified this week  •  %d trading guilds indexed", verifiedWk, total, knownGuilds))
        end
    end
end
