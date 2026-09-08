--[[
    Fissal's Cogwork Relay (FissalRelay)
    Crafted by Echo & Fissal for Castle Echo and the Redfur Guilds.
    
    Unified high-fidelity LibHistoire event capture, In-Person Kiosk Ground Recon,
    and All-Guild Staff Management Utilities.
]]--

FissalRelay = FissalRelay or {}
local FR = FissalRelay

FR.name = "FissalRelay"
FR.version = "1.2.2"
FR.author = "Echo & Fissal"

-- Defaults for SavedVariables
local DEFAULT_SAVED_VARS = {
    version = 2,
    historyDepthDays = 30,
    lastSeenEventId = {},
    sales = {},
    kiosks = {},
    staff = {
        bankDeposits = {},
        rosterSnapshots = {},
        inactivityAudits = {},
        bids = {},
        bidRefunds = {},
    },
    settings = {
        chatAnnouncements = true,
        soundEffects = true,
        trackAllGuilds = true,
        autoRosterSnapshotOnLogin = true,
        announceKioskRecon = true,
        showHud = true,
        hudPos = { x = 0, y = 0 },
        bumpGuilds = {},
        bumperPos = { x = 0, y = 0 },
        showBumper = true,
        bankGuilds = {},
    }
}

FR.processors = {}
FR.isReady = false
FR.bumpQueue = {}
FR.currentBumpIndex = 0
FR.isBumping = false
FR.bumpCurrentPage = 0
FR.bumpItemsScannedInGuild = 0
FR.bumpTotalItemsScanned = 0

local function ColorText(text, colorHex)
    return string.format("|c%s%s|r", colorHex or "FF9900", text)
end
FR.ColorText = ColorText

local function PrintChat(msg)
    if not FR.savedVars or FR.savedVars.settings.chatAnnouncements then
        local tag = ColorText("[Fissal]", "FF9900")
        df("%s %s", tag, msg)
    end
end
FR.PrintChat = PrintChat

local function PlayFissalSound()
    if not FR.savedVars or FR.savedVars.settings.soundEffects then
        PlaySound(SOUNDS.LOCKPICKING_UNLOCKED)
    end
end
FR.PlayFissalSound = PlayFissalSound

--[[ =========================================================================
     DUPLICATE CHECKING & SALES INGEST
========================================================================= ]]--

function FR:AddSale(event, guildId)
    local info = event:GetEventInfo()
    if not info then return false end

    local eventId = event:GetEventId()
    local eventIdStr = tostring(eventId)
    if self.savedVars.sales[eventIdStr] then
        return false -- Duplicate already saved
    end

    local guildName = GetGuildName(guildId)
    local seller = info.sellerDisplayName or ""
    local buyer = info.buyerDisplayName or ""
    local price = info.price or 0
    local quantity = info.quantity or 1
    local itemLink = info.itemLink or ""
    local eventTime = event:GetEventTimestampS() or GetTimeStamp()

    -- Check if buyer was an outsider (kiosk sale)
    local wasKiosk = true
    if buyer ~= "" and GetGuildMemberIndexFromDisplayName(guildId, buyer) then
        wasKiosk = false
    end

    self.savedVars.sales[eventIdStr] = {
        id = eventIdStr,
        timestamp = eventTime,
        guildId = guildId,
        guildName = guildName,
        seller = seller,
        buyer = buyer,
        itemLink = itemLink,
        price = price,
        quant = quantity,
        wasKiosk = wasKiosk,
    }

    -- Keep track of latest event ID for this guild
    local currentLast = self.savedVars.lastSeenEventId[guildId]
    if not currentLast or eventIdStr > currentLast then
        self.savedVars.lastSeenEventId[guildId] = eventIdStr
    end

    return true
end

function FR:AddBankDeposit(event, guildId)
    local info = event:GetEventInfo()
    if not info or info.currencyType ~= CURT_MONEY then return false end

    local eventId = event:GetEventId()
    local eventIdStr = tostring(eventId)
    if self.savedVars.staff.bankDeposits[eventIdStr] then
        return false
    end

    local guildName = GetGuildName(guildId)
    local eventTime = event:GetEventTimestampS() or GetTimeStamp()

    self.savedVars.staff.bankDeposits[eventIdStr] = {
        id = eventIdStr,
        timestamp = eventTime,
        guildId = guildId,
        guildName = guildName,
        depositor = info.displayName or "",
        amount = info.amount or 0,
        rawType = CURT_MONEY,
    }

    return true
end

function FR:AddKioskBid(event, guildId)
    local info = event:GetEventInfo()
    if not info then return false end

    local eventId = event:GetEventId()
    local eventIdStr = tostring(eventId)
    if not self.savedVars.staff.bids then self.savedVars.staff.bids = {} end
    if self.savedVars.staff.bids[eventIdStr] then
        return false
    end

    local guildName = GetGuildName(guildId)
    local eventTime = event:GetEventTimestampS() or GetTimeStamp()
    local bidder = info.displayName or "Staff"
    local amount = info.amount or 0
    local kioskName = info.kioskName or "Unknown Kiosk"

    self.savedVars.staff.bids[eventIdStr] = {
        id = eventIdStr,
        timestamp = eventTime,
        guildId = guildId,
        guildName = guildName,
        bidder = bidder,
        kioskName = kioskName,
        amount = amount,
        status = "Pending",
    }

    if self.savedVars.settings.chatAnnouncements then
        PrintChat(string.format("Kiosk Bid Logged: %s bid %s gold on %s for %s",
            ColorText(bidder, "FFFFFF"),
            ColorText(ZO_LocalizeDecimalNumber(amount), "FFD700"),
            ColorText(kioskName, "00FFCC"),
            ColorText(guildName, "00FF00")))
        PlayFissalSound()
    end

    return true
end

function FR:AddKioskBidRefund(event, guildId)
    local info = event:GetEventInfo()
    if not info then return false end

    local eventId = event:GetEventId()
    local eventIdStr = tostring(eventId)
    if not self.savedVars.staff.bidRefunds then self.savedVars.staff.bidRefunds = {} end
    if self.savedVars.staff.bidRefunds[eventIdStr] then
        return false
    end

    local guildName = GetGuildName(guildId)
    local eventTime = event:GetEventTimestampS() or GetTimeStamp()
    local kioskName = info.kioskName or "Unknown Kiosk"
    local amount = info.amount or 0

    self.savedVars.staff.bidRefunds[eventIdStr] = {
        id = eventIdStr,
        timestamp = eventTime,
        guildId = guildId,
        guildName = guildName,
        kioskName = kioskName,
        amount = amount,
    }

    -- Mark matching pending bid as Refunded (Lost)
    if self.savedVars.staff.bids then
        for _, bid in pairs(self.savedVars.staff.bids) do
            if bid.guildId == guildId and bid.kioskName == kioskName and bid.status == "Pending" and bid.timestamp <= eventTime then
                bid.status = "Refunded (Lost)"
                bid.refundTime = eventTime
            end
        end
    end

    return true
end

function FR:AddKioskPurchase(event, guildId)
    local info = event:GetEventInfo()
    if not info then return false end

    local eventId = event:GetEventId()
    local eventIdStr = tostring(eventId)
    if not self.savedVars.staff.bids then self.savedVars.staff.bids = {} end

    local guildName = GetGuildName(guildId)
    local eventTime = event:GetEventTimestampS() or GetTimeStamp()
    local buyer = info.displayName or "Staff"
    local amount = info.amount or 0
    local kioskName = info.kioskName or "Unknown Kiosk"

    self.savedVars.staff.bids[eventIdStr] = {
        id = eventIdStr,
        timestamp = eventTime,
        guildId = guildId,
        guildName = guildName,
        bidder = buyer,
        kioskName = kioskName,
        amount = amount,
        status = "Direct Purchase (Won)",
    }

    return true
end

--[[ =========================================================================
     AUTOMATIC PRUNING ENGINE & DATA RETENTION
========================================================================= ]]--

function FR:PruneExpiredData(isManual)
    if not self.savedVars or not self.savedVars.sales then return 0 end

    local historyDepthDays = tonumber(self.savedVars.historyDepthDays) or 30
    local cutoff = GetTimeStamp() - (historyDepthDays * 86400)
    local prunedSalesCount = 0

    for id, sale in pairs(self.savedVars.sales) do
        local ts = tonumber(sale.timestamp)
        if ts and ts < cutoff then
            self.savedVars.sales[id] = nil
            prunedSalesCount = prunedSalesCount + 1
        end
    end

    if prunedSalesCount > 0 then
        PrintChat(string.format("Pruning Engine: Purged %s expired sales records (> %d days old).",
            ColorText(ZO_LocalizeDecimalNumber(prunedSalesCount), "FFD700"), historyDepthDays))
        PlayFissalSound()
    elseif isManual then
        PrintChat(string.format("Pruning Engine: All sales records are within the %d-day retention window (0 purged).",
            historyDepthDays))
    end

    if self.UpdateHUD then self:UpdateHUD() end
    return prunedSalesCount
end

--[[ =========================================================================
     IN-PERSON TRADER KIOSK GROUND RECON
========================================================================= ]]--

function FR:RecordKioskObservation()
    local guildId, guildName = GetCurrentTradingHouseGuildDetails()
    if not guildName or guildName == "" then
        guildId, guildName = GetTradingHouseGuildDetails(1)
    end
    local traderName = GetUnitName("interact")

    if not traderName or traderName == "" then return end
    if not guildName or guildName == "" then
        guildName = "No Hiring Guild (Vacant)"
        guildId = 0
    end

    local zoneName = GetUnitZone("player")
    if not zoneName or zoneName == "" then
        zoneName = GetPlayerLocationName() or "Tamriel"
    end
    local locationName = GetPlayerLocationName()
    if not locationName or locationName == "" then
        locationName = zoneName
    end

    local x, y = GetMapPlayerPosition("player")
    local nowTs = GetTimeStamp()
    local account = GetDisplayName()

    local prevObs = self.savedVars.kiosks[traderName]
    local isNewOrChanged = not prevObs or prevObs.guildName ~= guildName

    self.savedVars.kiosks[traderName] = {
        trader = traderName,
        guildId = guildId,
        guildName = guildName,
        zone = zoneName,
        city = locationName,
        x = string.format("%.4f", x or 0),
        y = string.format("%.4f", y or 0),
        timestamp = nowTs,
        observedBy = account,
    }

    if isNewOrChanged and self.savedVars.settings.announceKioskRecon then
        PrintChat(string.format("Kiosk Recon: %s in %s (%s) -> %s",
            ColorText(traderName, "FFFFFF"),
            ColorText(locationName, "00FFCC"),
            zoneName,
            ColorText(guildName, "00FF00")))
        PlayFissalSound()
    end
end

function FR:ScanOwnedKiosks()
    local numGuilds = GetNumGuilds()
    local found = 0
    for i = 1, numGuilds do
        local guildId = GetGuildId(i)
        local guildName = GetGuildName(guildId)
        local traderName = GetGuildOwnedKioskInfo(guildId)

        if traderName and traderName ~= "" then
            local prevObs = self.savedVars.kiosks[traderName]
            if not prevObs then
                self.savedVars.kiosks[traderName] = {
                    trader = traderName,
                    guildId = guildId,
                    guildName = guildName,
                    zone = "Owned Guild Stall",
                    city = "Guild Ledger",
                    x = "0.0000",
                    y = "0.0000",
                    timestamp = GetTimeStamp(),
                    observedBy = GetDisplayName(),
                }
            else
                prevObs.guildId = guildId
                prevObs.guildName = guildName
            end

            -- Correlate with pending bids: if we own this trader, mark our bid as Won!
            if self.savedVars.staff and self.savedVars.staff.bids then
                for _, bid in pairs(self.savedVars.staff.bids) do
                    if bid.guildId == guildId and bid.kioskName == traderName and bid.status == "Pending" then
                        bid.status = "Won"
                    end
                end
            end

            found = found + 1
        end
    end
    return found
end

--[[ =========================================================================
     STAFF TOOLS: ROSTER, INACTIVES & RAFFLES / DUES
========================================================================= ]]--

function FR:TakeRosterSnapshot(targetGuildId)
    local numGuilds = GetNumGuilds()
    local snapped = 0

    if not self.savedVars.staff then self.savedVars.staff = {} end
    if not self.savedVars.staff.rosterSnapshots then self.savedVars.staff.rosterSnapshots = {} end

    for i = 1, numGuilds do
        local guildId = GetGuildId(i)
        if not targetGuildId or targetGuildId == guildId then
            local guildName = GetGuildName(guildId)
            local memberCount = GetNumGuildMembers(guildId)
            local hiredTrader = GetGuildOwnedKioskInfo(guildId) or "None"
            local members = {}

            for m = 1, memberCount do
                local name, note, rankIndex, isOnline, secsSinceLogoff = GetGuildMemberInfo(guildId, m)
                local rankName = GetGuildRankCustomName(guildId, rankIndex)
                if not rankName or rankName == "" then
                    rankName = GetDefaultGuildRankName(guildId, rankIndex)
                end

                members[name] = {
                    rank = rankName,
                    rankIndex = rankIndex,
                    note = note or "",
                    secsSinceLogoff = secsSinceLogoff,
                    isOnline = isOnline,
                }
            end

            self.savedVars.staff.rosterSnapshots[guildId] = {
                guildName = guildName,
                hiredTrader = hiredTrader,
                timestamp = GetTimeStamp(),
                memberCount = memberCount,
                members = members,
            }
            snapped = snapped + 1
        end
    end

    self:ScanOwnedKiosks()
    return snapped
end

function FR:AuditInactives(guildIndex, minDays)
    minDays = tonumber(minDays) or 14
    guildIndex = tonumber(guildIndex) or 1

    local numGuilds = GetNumGuilds()
    if guildIndex < 1 or guildIndex > numGuilds then
        PrintChat(string.format("Invalid guild index %d (you are in %d guilds).", guildIndex, numGuilds))
        return
    end

    local guildId = GetGuildId(guildIndex)
    local guildName = GetGuildName(guildId)
    local memberCount = GetNumGuildMembers(guildId)
    local cutoffSecs = minDays * 86400
    local inactives = {}

    for m = 1, memberCount do
        local name, note, rankIndex, isOnline, secsSinceLogoff = GetGuildMemberInfo(guildId, m)
        if not isOnline and secsSinceLogoff >= cutoffSecs then
            local days = math.floor(secsSinceLogoff / 86400)
            local rankName = GetGuildRankCustomName(guildId, rankIndex)
            table.insert(inactives, { name = name, days = days, rank = rankName, note = note })
        end
    end

    table.sort(inactives, function(a, b) return a.days > b.days end)

    self.savedVars.staff.inactivityAudits[guildName] = {
        guildId = guildId,
        minDays = minDays,
        auditedAt = GetTimeStamp(),
        totalMembers = memberCount,
        inactiveCount = #inactives,
        members = inactives,
    }

    PrintChat(string.format("=== Inactivity Audit: %s (%d+ days) ===", ColorText(guildName, "00FFCC"), minDays))
    PrintChat(string.format("Found %s inactive members out of %d total.", ColorText(tostring(#inactives), "FF5555"), memberCount))

    local showMax = math.min(#inactives, 5)
    for i = 1, showMax do
        local m = inactives[i]
        df("  #%d %s (%d days inactive, %s)", i, ColorText(m.name, "FFFFFF"), m.days, m.rank or "Member")
    end
    if #inactives > showMax then
        df("  ...and %d more. Full audit saved to SavedVariables for export!", #inactives - showMax)
    end
    PlayFissalSound()
end

function FR:AuditBankDues(guildIndex, limitDays)
    limitDays = tonumber(limitDays) or 7
    guildIndex = tonumber(guildIndex) or 1

    local numGuilds = GetNumGuilds()
    if guildIndex < 1 or guildIndex > numGuilds then
        PrintChat(string.format("Invalid guild index %d.", guildIndex))
        return
    end

    local guildId = GetGuildId(guildIndex)
    local guildName = GetGuildName(guildId)

    if not self:CanTrackGuildBank(guildId) then
        PrintChat(string.format("Cannot audit bank dues for %s: You do not have permission to view bank deposits in this guild.",
            ColorText(guildName, "FF5555")))
        return
    end

    local nowTs = GetTimeStamp()
    local cutoffTs = nowTs - (limitDays * 86400)

    local depositsByMember = {}
    local totalGold = 0
    local count = 0

    for _, dep in pairs(self.savedVars.staff.bankDeposits) do
        if dep.guildId == guildId and dep.timestamp >= cutoffTs then
            local depositor = dep.depositor
            depositsByMember[depositor] = (depositsByMember[depositor] or 0) + dep.amount
            totalGold = totalGold + dep.amount
            count = count + 1
        end
    end

    local sorted = {}
    for member, amt in pairs(depositsByMember) do
        table.insert(sorted, { member = member, amount = amt })
    end
    table.sort(sorted, function(a, b) return a.amount > b.amount end)

    PrintChat(string.format("=== Weekly Bank Audit: %s (Past %d Days) ===", ColorText(guildName, "00FFCC"), limitDays))
    PrintChat(string.format("Total: %s gold across %d deposits from %d contributors.",
        ColorText(ZO_LocalizeDecimalNumber(totalGold), "FFD700"), count, #sorted))

    local showMax = math.min(#sorted, 5)
    for i = 1, showMax do
        local entry = sorted[i]
        df("  #%d %s: %s gold", i, ColorText(entry.member, "FFFFFF"), ColorText(ZO_LocalizeDecimalNumber(entry.amount), "FFD700"))
    end
    PlayFissalSound()
end

--[[ =========================================================================
     LIBHISTOIRE PROCESSORS & TURBO PUMP ENGINE
========================================================================= ]]--

function FR:CanTrackGuildBank(guildId)
    if not guildId or guildId == 0 then return false end

    -- 1. Check user preference override in settings if present
    if self.savedVars and self.savedVars.settings and self.savedVars.settings.bankGuilds then
        local userVal = self.savedVars.settings.bankGuilds[guildId]
        if userVal == false then
            return false
        end
    end

    -- 2. Guild Master check (always has full rights)
    if IsPlayerGuildMaster and IsPlayerGuildMaster(guildId) then
        return true
    end

    -- 3. Guild privilege check: Bank unlocked (10+ members)
    if DoesGuildHavePrivilege and not DoesGuildHavePrivilege(guildId, GUILD_PRIVILEGE_BANK_DEPOSIT) then
        return false
    end

    -- 4. Player rank permission check: View Guild Bank Gold
    -- In ESO, GUILD_PERMISSION_BANK_VIEW_GOLD controls visibility of bank gold and deposit/withdraw history
    if DoesPlayerHaveGuildPermission and GUILD_PERMISSION_BANK_VIEW_GOLD then
        if not DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_BANK_VIEW_GOLD) then
            return false
        end
    end

    return true
end

function FR:FixLibHistoire()
    if not LibHistoire or not LibHistoire.internal or not LibHistoire.internal.historyCache then return end
    local cacheManager = LibHistoire.internal.historyCache
    local numGuilds = GetNumGuilds()

    for i = 1, numGuilds do
        local guildId = GetGuildId(i)
        for _, category in ipairs({ GUILD_HISTORY_EVENT_CATEGORY_TRADER, GUILD_HISTORY_EVENT_CATEGORY_BANKED_CURRENCY }) do
            local cache = cacheManager:GetCategoryCache(guildId, category)
            if cache then
                local isBank = (category == GUILD_HISTORY_EVENT_CATEGORY_BANKED_CURRENCY)
                local canTrack = not isBank or self:CanTrackGuildBank(guildId)

                if canTrack then
                    -- 1. Ensure self.guild exists on the cache object as a runtime fallback for LibHistoire bug
                    cache.guild = guildId
                    cache.guildId = guildId

                    -- 2. Clear initialRequestTime lockout if managed range not established yet (fixes 7-day stall)
                    if cache.saveData and cache.saveData.initialRequestTime and not cache:GetOldestManagedEventInfo() then
                        cache.saveData.initialRequestTime = nil
                    end

                    -- 3. Ensure category is auto
                    if cache.GetRequestMode and cache.SetRequestMode and cache:GetRequestMode() == "off" then
                        cache:SetRequestMode("auto")
                    end
                else
                    -- No permission for bank deposits: ensure category is OFF so LibHistoire doesn't hammer server
                    if cache.GetRequestMode and cache.SetRequestMode and cache:GetRequestMode() ~= "off" then
                        cache:SetRequestMode("off")
                    end
                    -- Clean up any stuck pending request for this forbidden category
                    if cache.DestroyRequest and cache.request then
                        cache:DestroyRequest()
                    end
                end
            end
        end
    end
end

function FR:PumpLibHistoire(isManual)
    if not LibHistoire or not LibHistoire.internal or not LibHistoire.internal.historyCache then
        if isManual then self.PrintChat("LibHistoire cache not ready.") end
        return 0
    end

    self:FixLibHistoire()

    local cacheManager = LibHistoire.internal.historyCache
    local requestManager = cacheManager.requestManager
    local numGuilds = GetNumGuilds()
    local unlinkedCount = 0
    local requestedCount = 0
    local skippedCount = 0

    for i = 1, numGuilds do
        local guildId = GetGuildId(i)
        for _, category in ipairs({ GUILD_HISTORY_EVENT_CATEGORY_TRADER, GUILD_HISTORY_EVENT_CATEGORY_BANKED_CURRENCY }) do
            if category == GUILD_HISTORY_EVENT_CATEGORY_BANKED_CURRENCY and not self:CanTrackGuildBank(guildId) then
                skippedCount = skippedCount + 1
            else
                local cache = cacheManager:GetCategoryCache(guildId, category)
                if cache then
                    if not cache:HasLinked() then
                        unlinkedCount = unlinkedCount + 1
                        if not cache:HasPendingRequest() then
                            cache:RequestMissingData()
                            requestedCount = requestedCount + 1
                        end
                    end
                end
            end
        end
    end

    if requestManager and requestManager.RequestSendNext then
        requestManager:RequestSendNext()
    end

    if isManual then
        if unlinkedCount == 0 then
            local skippedNote = (skippedCount > 0) and string.format(" (%d bank channel(s) skipped: no permission)", skippedCount) or ""
            self.PrintChat(string.format("All active history channels are |c00FF00100%% linked|r and up-to-date!%s", skippedNote))
        else
            self.PrintChat(string.format("Turbo Pump: Kicked %s request(s). %s channel(s) linking in progress.",
                self.ColorText(tostring(requestedCount), "FFD700"), self.ColorText(tostring(unlinkedCount), "00FFCC")))
        end
        self.PlayFissalSound()
    end

    if self.UpdateHUD then self:UpdateHUD() end
    return unlinkedCount
end

function FR:StartHistoryPumper()
    if self.pumperRegistered then return end
    self.pumperRegistered = true

    local function Heartbeat()
        if not FR.isReady then
            zo_callLater(Heartbeat, 2500)
            return
        end

        local unlinked = FR:PumpLibHistoire(false)
        -- If channels are still unlinked, pump actively every 2.5 seconds (matching server rate limit)
        -- Once all channels are 100% linked, relax to 30 seconds
        local nextDelay = (unlinked and unlinked > 0) and 2500 or 30000
        zo_callLater(Heartbeat, nextDelay)
    end

    zo_callLater(Heartbeat, 3000)
end

function FR:SetupProcessors()
    if not LibHistoire or not LibHistoire.IsReady or not LibHistoire:IsReady() then
        return
    end

    local function EnsureCategoryAuto(guildId, category)
        if LibHistoire.internal and LibHistoire.internal.historyCache then
            local cache = LibHistoire.internal.historyCache:GetCategoryCache(guildId, category)
            if cache and cache.GetRequestMode and cache.SetRequestMode then
                if cache:GetRequestMode() == "off" then
                    cache:SetRequestMode("auto")
                end
            end
        end
    end

    local numGuilds = GetNumGuilds()
    for i = 1, numGuilds do
        local guildId = GetGuildId(i)

        -- Automatically wake up trader sales category
        EnsureCategoryAuto(guildId, GUILD_HISTORY_EVENT_CATEGORY_TRADER)

        -- 1. Trader Sales Processor
        if not self.processors["trader_" .. guildId] then
            local processor = LibHistoire:CreateGuildHistoryProcessor(
                guildId, GUILD_HISTORY_EVENT_CATEGORY_TRADER, "FissalRelay_Trader"
            )

            if processor then
                local lastId = self.savedVars.lastSeenEventId[guildId]
                if lastId then
                    local converted = tonumber(lastId)
                    if converted then processor:SetAfterEventId(converted) end
                else
                    local daysCutoff = GetTimeStamp() - (self.savedVars.historyDepthDays * 86400)
                    processor:SetAfterEventTime(daysCutoff)
                end

                processor:SetEventCallback(function(event)
                    if event:GetEventType() == GUILD_HISTORY_TRADER_EVENT_ITEM_SOLD then
                        FR:AddSale(event, guildId)
                    end
                end)

                processor:Start()
                self.processors["trader_" .. guildId] = processor
            end
        end

        -- 2. Bank Currency Processor (for Staff Raffle / Dues tracking)
        if self:CanTrackGuildBank(guildId) then
            EnsureCategoryAuto(guildId, GUILD_HISTORY_EVENT_CATEGORY_BANKED_CURRENCY)

            if not self.processors["bank_" .. guildId] then
                local bankProc = LibHistoire:CreateGuildHistoryProcessor(
                    guildId, GUILD_HISTORY_EVENT_CATEGORY_BANKED_CURRENCY, "FissalRelay_Bank"
                )

                if bankProc then
                    local daysCutoff = GetTimeStamp() - (self.savedVars.historyDepthDays * 86400)
                    bankProc:SetAfterEventTime(daysCutoff)

                    bankProc:SetEventCallback(function(event)
                        local eventType = event:GetEventType()
                        if eventType == GUILD_HISTORY_BANKED_CURRENCY_EVENT_DEPOSITED then
                            FR:AddBankDeposit(event, guildId)
                        elseif eventType == GUILD_HISTORY_BANKED_CURRENCY_EVENT_KIOSK_BID then
                            FR:AddKioskBid(event, guildId)
                        elseif eventType == GUILD_HISTORY_BANKED_CURRENCY_EVENT_KIOSK_BID_REFUND then
                            FR:AddKioskBidRefund(event, guildId)
                        elseif eventType == GUILD_HISTORY_BANKED_CURRENCY_EVENT_KIOSK_PURCHASED then
                            FR:AddKioskPurchase(event, guildId)
                        end
                    end)

                    bankProc:Start()
                    self.processors["bank_" .. guildId] = bankProc
                end
            end
        else
            -- If bank processor was previously active on this guild, stop and clean it up
            if self.processors["bank_" .. guildId] then
                local bankProc = self.processors["bank_" .. guildId]
                if bankProc.Stop then bankProc:Stop() end
                self.processors["bank_" .. guildId] = nil
            end

            -- Ensure request mode is OFF in LibHistoire
            if LibHistoire.internal and LibHistoire.internal.historyCache then
                local cache = LibHistoire.internal.historyCache:GetCategoryCache(guildId, GUILD_HISTORY_EVENT_CATEGORY_BANKED_CURRENCY)
                if cache and cache.SetRequestMode then
                    cache:SetRequestMode("off")
                    if cache.DestroyRequest and cache.request then
                        cache:DestroyRequest()
                    end
                end
            end
        end
    end

    self.isReady = true
    self:FixLibHistoire()
    self:StartHistoryPumper()
end

--[[ =========================================================================
     TTC GUILD BUMPER SCAN ENGINE
========================================================================= ]]--

function FR:IsGuildBumpSelected(guildId)
    if not self.savedVars or not self.savedVars.settings.bumpGuilds then return true end
    local val = self.savedVars.settings.bumpGuilds[guildId]
    if val == nil then
        local kiosk = GetGuildOwnedKioskInfo and GetGuildOwnedKioskInfo(guildId)
        return kiosk ~= nil
    end
    return val == true
end

function FR:SetGuildBumpSelected(guildId, isSelected)
    if not self.savedVars then return end
    if not self.savedVars.settings.bumpGuilds then
        self.savedVars.settings.bumpGuilds = {}
    end
    self.savedVars.settings.bumpGuilds[guildId] = isSelected
end

function FR:StartBump()
    if self.isBumping then
        PrintChat("Bumping is already active. Click [Cancel] to halt.")
        return
    end

    if not IsTradingHouseOpen or not IsTradingHouseOpen() then
        PrintChat("Please open the Guild Store at a Banker or Guild Trader first.")
        return
    end

    if not TamrielTradeCentre then
        PrintChat("TamrielTradeCentre addon is required for bumping.")
        return
    end

    if TamrielTradeCentre.Settings then
        TamrielTradeCentre.Settings.EnableAutoRecordStoreEntries = true
    end

    local queue = {}
    local numTradingGuilds = GetNumTradingHouseGuilds()

    if numTradingGuilds <= 1 then
        local guildId, guildName = GetCurrentTradingHouseGuildDetails()
        if guildId and guildId ~= 0 then
            table.insert(queue, { id = guildId, name = guildName })
        end
    else
        local numGuilds = GetNumGuilds()
        for i = 1, numGuilds do
            local gId = GetGuildId(i)
            local gName = GetGuildName(gId)
            if self:IsGuildBumpSelected(gId) then
                table.insert(queue, { id = gId, name = gName })
            end
        end
    end

    if #queue == 0 then
        PrintChat("No guilds selected to bump. Check at least one guild in the Bumper panel.")
        return
    end

    self.bumpQueue = queue
    self.currentBumpIndex = 1
    self.bumpTotalItemsScanned = 0
    self.isBumping = true

    PrintChat(string.format("Beginning TTC Bump across %s guild(s)...", ColorText(tostring(#queue), "00FFCC")))
    if self.UpdateBumperUI then self:UpdateBumperUI() end
    self:StepNextBumpGuild()
end

function FR:CancelBump()
    if self.isBumping then
        self.isBumping = false
        self.bumpQueue = {}
        PrintChat("TTC Bumping halted by user.")
        if self.UpdateBumperUI then self:UpdateBumperUI() end
    end
end

function FR:StepNextBumpGuild()
    if not self.isBumping then return end

    if self.currentBumpIndex > #self.bumpQueue then
        self.isBumping = false
        PlayFissalSound()
        PrintChat(string.format("All %d guild(s) successfully bumped! Total %s listings recorded for TTC. Hit [ReloadUI] to upload.",
            #self.bumpQueue, ColorText(ZO_LocalizeDecimalNumber(self.bumpTotalItemsScanned), "FFD700")))
        if self.UpdateBumperUI then self:UpdateBumperUI() end
        return
    end

    local entry = self.bumpQueue[self.currentBumpIndex]
    self.bumpCurrentPage = 0
    self.bumpItemsScannedInGuild = 0

    PrintChat(string.format("[%d/%d] Switching store to %s...",
        self.currentBumpIndex, #self.bumpQueue, ColorText(entry.name, "00FFCC")))
    if self.UpdateBumperUI then self:UpdateBumperUI() end

    SelectTradingHouseGuildId(entry.id)

    zo_callLater(function()
        if not self.isBumping then return end
        if TRADING_HOUSE_SEARCH and TRADING_HOUSE_SEARCH.ResetAllSearchData then
            TRADING_HOUSE_SEARCH:ResetAllSearchData()
        end
        self:ExecuteBumpPage()
    end, 1500)
end

function FR:ExecuteBumpPage()
    if not self.isBumping then return end

    local delay = math.max(GetTradingHouseCooldownRemaining() + 1000, 3000)
    zo_callLater(function()
        if not self.isBumping then return end
        ExecuteTradingHouseSearch(self.bumpCurrentPage)
    end, delay)
end

function FR:OnTradingHouseSearchResultsForBump()
    if not self.isBumping then return end

    local numItemsOnPage, currentPage, hasMorePages = GetTradingHouseSearchResultsInfo()
    local entry = self.bumpQueue[self.currentBumpIndex]
    local guildName = entry and entry.name or "Guild"

    if numItemsOnPage and numItemsOnPage > 0 then
        self.bumpItemsScannedInGuild = self.bumpItemsScannedInGuild + numItemsOnPage
        self.bumpTotalItemsScanned = self.bumpTotalItemsScanned + numItemsOnPage

        if self.UpdateBumperUI then
            self:UpdateBumperUI(string.format("Bumping %s: Page %d (%d items)...",
                guildName, currentPage + 1, self.bumpItemsScannedInGuild))
        end

        if hasMorePages then
            self.bumpCurrentPage = currentPage + 1
            self:ExecuteBumpPage()
        else
            PrintChat(string.format("✓ %s bumped (%d items).", ColorText(guildName, "00FFCC"), self.bumpItemsScannedInGuild))
            self.currentBumpIndex = self.currentBumpIndex + 1
            zo_callLater(function() self:StepNextBumpGuild() end, 1000)
        end
    else
        PrintChat(string.format("✓ %s bumped (%d items).", ColorText(guildName, "00FFCC"), self.bumpItemsScannedInGuild))
        self.currentBumpIndex = self.currentBumpIndex + 1
        zo_callLater(function() self:StepNextBumpGuild() end, 1000)
    end
end

--[[ =========================================================================
     SLASH COMMANDS & CHAT INTERFACE
========================================================================= ]]--

function FR:HandleSlashCommand(arg)
    local args = {}
    for word in string.gmatch(arg or "", "%S+") do
        table.insert(args, word)
    end

    local cmd = string.lower(args[1] or "")

    if cmd == "" or cmd == "help" then
        PrintChat("Clockwork Alfiq Artificer at your service! Commands:")
        PrintChat("/fissal ui (or /fissal hud) - Toggle floating Fissal status meter HUD.")
        PrintChat("/fissal bump (or /fissal ttc) - Open TTC guild bumper / bump selected guilds.")
        PrintChat("/fissal status             - Check active relay listeners and stored records.")
        PrintChat("/fissal prune              - Purge sales older than history depth days.")
        PrintChat("/fissal scout              - View in-person scouted trader kiosks.")
        PrintChat("/fissal bids               - View recent kiosk bids placed, won, and refunded.")
        PrintChat("/fissal inactives [g#] [d] - Audit members inactive > d days (default: guild 1, 14 days).")
        PrintChat("/fissal dues [g#] [days]   - Audit bank deposits / raffle gold for guild.")
        PrintChat("/fissal turbo               - Force turbo-pump all LibHistoire channels at max rate limit.")
        PrintChat("/fissal sync                - Turbo pump history, scan kiosks, and take roster snapshots.")
    elseif cmd == "ui" or cmd == "hud" then
        if self.ToggleHUD then
            self:ToggleHUD()
        else
            PrintChat("HUD interface module not ready.")
        end
    elseif cmd == "bump" or cmd == "ttc" then
        if self.ToggleBumperUI then
            self:ToggleBumperUI()
        else
            self:StartBump()
        end
    elseif cmd == "status" then
        local saleCount = NonContiguousCount(self.savedVars.sales)
        local depositCount = NonContiguousCount(self.savedVars.staff and self.savedVars.staff.bankDeposits or {})
        local kioskCount = NonContiguousCount(self.savedVars.kiosks or {})
        local bidCount = NonContiguousCount(self.savedVars.staff and self.savedVars.staff.bids or {})
        local rosterCount = NonContiguousCount(self.savedVars.staff and self.savedVars.staff.rosterSnapshots or {})
        local bankPermCount, numGuilds = 0, GetNumGuilds()
        for i = 1, numGuilds do
            if self:CanTrackGuildBank(GetGuildId(i)) then
                bankPermCount = bankPermCount + 1
            end
        end

        PrintChat(string.format("Relay Status: %s Sales • %s Kiosks • %s Bids • %s Rosters • %s Bank Deposits (%d/%d Guilds Monitored)",
            ColorText(ZO_LocalizeDecimalNumber(saleCount), "00FFCC"),
            ColorText(tostring(kioskCount), "00FF00"),
            ColorText(tostring(bidCount), "FFD700"),
            ColorText(tostring(rosterCount), "00FFFF"),
            ColorText(ZO_LocalizeDecimalNumber(depositCount), "FFAA00"),
            bankPermCount, numGuilds))
        if self.UpdateHUD then self:UpdateHUD() end
        PlayFissalSound()
    elseif cmd == "prune" then
        self:PruneExpiredData(true)
    elseif cmd == "scout" or cmd == "kiosks" then
        local kioskCount = NonContiguousCount(self.savedVars.kiosks or {})
        PrintChat(string.format("=== Ground Recon: %s Verified Kiosks ===", ColorText(tostring(kioskCount), "00FF00")))
        local list = {}
        for trader, data in pairs(self.savedVars.kiosks or {}) do
            table.insert(list, data)
        end
        table.sort(list, function(a, b) return a.timestamp > b.timestamp end)
        local showMax = math.min(#list, 6)
        for i = 1, showMax do
            local k = list[i]
            df("  • %s (%s, %s) -> %s", ColorText(k.trader, "FFFFFF"), k.city or "?", k.zone or "?", ColorText(k.guildName, "00FFCC"))
        end
        if #list > showMax then
            df("  ...and %d more scouted kiosks.", #list - showMax)
        end
        if self.UpdateHUD then self:UpdateHUD() end
        PlayFissalSound()
    elseif cmd == "bids" or cmd == "bidding" then
        local bidCount = NonContiguousCount(self.savedVars.staff and self.savedVars.staff.bids or {})
        PrintChat(string.format("=== Guild Kiosk Bids Ledger: %s Recorded ===", ColorText(tostring(bidCount), "FFD700")))
        local list = {}
        for _, b in pairs(self.savedVars.staff and self.savedVars.staff.bids or {}) do
            table.insert(list, b)
        end
        table.sort(list, function(a, b) return a.timestamp > b.timestamp end)
        local showMax = math.min(#list, 8)
        for i = 1, showMax do
            local b = list[i]
            local statusColor = (b.status == "Won" or b.status == "Direct Purchase (Won)") and "00FF00" or (b.status == "Refunded (Lost)" and "FF5555" or "FFD700")
            df("  • [%s] %s: %s gold by %s -> %s (%s)",
                ColorText(b.guildName or "Guild", "00FFCC"),
                ColorText(b.kioskName or "Kiosk", "FFFFFF"),
                ColorText(ZO_LocalizeDecimalNumber(b.amount or 0), "FFD700"),
                b.bidder or "Staff",
                ColorText(b.status or "Pending", statusColor),
                ZO_FormatDurationAgo(GetTimeStamp() - (b.timestamp or GetTimeStamp())))
        end
        if #list > showMax then
            df("  ...and %d more historical bids.", #list - showMax)
        end
        if self.UpdateHUD then self:UpdateHUD() end
        PlayFissalSound()
    elseif cmd == "inactives" then
        local gIdx = tonumber(args[2]) or 1
        local days = tonumber(args[3]) or 14
        self:AuditInactives(gIdx, days)
    elseif cmd == "dues" or cmd == "bank" then
        local gIdx = tonumber(args[2]) or 1
        local days = tonumber(args[3]) or 7
        self:AuditBankDues(gIdx, days)
    elseif cmd == "turbo" or cmd == "pump" then
        self.PrintChat("Activating Fissal Clockwork Turbo Pumper...")
        self:PumpLibHistoire(true)
    elseif cmd == "sync" then
        self.PrintChat("Aligning clockwork dials and history pipelines...")
        self:SetupProcessors()
        self:PumpLibHistoire(true)
        local snapped = self:TakeRosterSnapshot()
        local kiosksFound = self:ScanOwnedKiosks()
        local bidCount = NonContiguousCount(self.savedVars.staff and self.savedVars.staff.bids or {})
        local saleCount = NonContiguousCount(self.savedVars.sales)
        self.PrintChat(string.format("All listeners aligned. %d rosters snapped, %d kiosks, %d bids recorded. %s sales ready for courier dispatch.",
            snapped, kiosksFound, bidCount, self.ColorText(tostring(saleCount), "00FFCC")))
        if self.UpdateHUD then self:UpdateHUD() end
        self.PlayFissalSound()
    elseif cmd == "roster" then
        local snapped = self:TakeRosterSnapshot()
        PrintChat(string.format("Roster snapshot captured for %d guild(s)! Saved for courier sync.", snapped))
        if self.UpdateHUD then self:UpdateHUD() end
        PlayFissalSound()
    else
        PrintChat(string.format("Unknown command '%s'. Type /fissal for assistance.", cmd))
    end
end

--[[ =========================================================================
     INITIALIZATION & EVENTS
========================================================================= ]]--

local function OnOpenTradingHouse(eventCode)
    FR:RecordKioskObservation()
end

local function OnTradingHouseResponse(eventCode, responseType, result)
    FR:RecordKioskObservation()
    if responseType == TRADING_HOUSE_RESULT_SEARCH_PENDING and FR.isBumping then
        FR:OnTradingHouseSearchResultsForBump()
    end
end

local function OnPlayerActivated(eventCode, initial)
    -- Run pruning safely delayed via zo_callLater so login/zoning remains buttery smooth
    zo_callLater(function()
        local function SafeRunPrune()
            if IsUnitInCombat and IsUnitInCombat("player") then
                -- Player is in combat, retry in 10 seconds
                zo_callLater(SafeRunPrune, 10000)
                return
            end
            local now = GetTimeStamp()
            if not FR.lastPruneTime or (now - FR.lastPruneTime) >= 3600 then
                FR.lastPruneTime = now
                FR:PruneExpiredData(false)
            end
        end
        SafeRunPrune()
    end, 15000)
end

local function OnAddOnLoaded(eventCode, addOnName)
    if addOnName ~= FR.name then return end
    EVENT_MANAGER:UnregisterForEvent(FR.name, EVENT_ADD_ON_LOADED)

    -- Initialize SavedVariables
    FR.savedVars = ZO_SavedVars:NewAccountWide(
        "FissalRelay_SavedVariables", 2, nil, DEFAULT_SAVED_VARS
    )
    if not FR.savedVars.kiosks then FR.savedVars.kiosks = {} end
    if not FR.savedVars.staff then FR.savedVars.staff = {} end
    if not FR.savedVars.staff.bankDeposits then FR.savedVars.staff.bankDeposits = {} end
    if not FR.savedVars.staff.bids then FR.savedVars.staff.bids = {} end
    if not FR.savedVars.staff.bidRefunds then FR.savedVars.staff.bidRefunds = {} end
    if not FR.savedVars.staff.rosterSnapshots then FR.savedVars.staff.rosterSnapshots = {} end
    if not FR.savedVars.staff.inactivityAudits then FR.savedVars.staff.inactivityAudits = {} end

    -- Register automatic pruning on player activation (safe, delayed, combat-guarded)
    EVENT_MANAGER:RegisterForEvent(FR.name .. "_Prune", EVENT_PLAYER_ACTIVATED, OnPlayerActivated)

    -- Register trading house kiosk recon events
    EVENT_MANAGER:RegisterForEvent(FR.name .. "_Kiosk", EVENT_OPEN_TRADING_HOUSE, OnOpenTradingHouse)
    EVENT_MANAGER:RegisterForEvent(FR.name .. "_KioskResp", EVENT_TRADING_HOUSE_RESPONSE_RECEIVED, OnTradingHouseResponse)

    -- Register guild rank update events to dynamically re-evaluate bank permissions
    EVENT_MANAGER:RegisterForEvent(FR.name .. "_RankUpdate", EVENT_GUILD_RANKS_UPDATED, function()
        if FR.isReady then FR:SetupProcessors() end
    end)
    EVENT_MANAGER:RegisterForEvent(FR.name .. "_MemberRank", EVENT_GUILD_MEMBER_RANK_CHANGED, function()
        if FR.isReady then FR:SetupProcessors() end
    end)

    -- Register slash commands
    SLASH_COMMANDS["/fissal"] = function(arg) FR:HandleSlashCommand(arg) end
    SLASH_COMMANDS["/fissalrelay"] = function(arg) FR:HandleSlashCommand(arg) end

    -- Setup LibHistoire integration when ready
    if LibHistoire and LibHistoire.OnReady then
        LibHistoire:OnReady(function()
            FR:SetupProcessors()
            PrintChat("Clockwork courier active. All guild trader lines monitored.")
        end)
    else
        zo_callLater(function()
            if LibHistoire and LibHistoire.IsReady and LibHistoire:IsReady() then
                FR:SetupProcessors()
            end
        end, 5000)
    end

    -- Auto roster snapshot and kiosk scan on login
    if FR.savedVars.settings.autoRosterSnapshotOnLogin then
        zo_callLater(function()
            FR:TakeRosterSnapshot()
            FR:ScanOwnedKiosks()
        end, 10000)
    end
end

EVENT_MANAGER:RegisterForEvent(FR.name, EVENT_ADD_ON_LOADED, OnAddOnLoaded)
