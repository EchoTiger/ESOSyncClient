--[[
    FissalRelay_RaffleMail.lua
    Raffle Mail Payout Assistant for Fissal's Cogwork Relay
    Crafted by Echo & Fissal for Fissal Relay and the Redfur Guilds.

    Features:
      • Docks alongside ZO_MailSend via ZO_SimpleSceneFragment
      • Dual-Guild selector tabs (Redfur Trading Post & Redfur Dealers)
      • Source Mode Toggle:
          - "Official Ledger" (default): Synchronized with Discord announcements and database
          - "Local Addon Roll": Live inspection of in-game RaffleGold.db SavedVariables
      • One-click auto-fill: sets recipient, subject, authentic ESO color markup body,
        and safely queues attached prize gold (with collision reset and balance check)
      • Subscribes to EVENT_MAIL_SEND_SUCCESS to idempotently record payout in SavedVariables
      • Visual locks ([PAID ✓]) preventing accidental duplicate gold payouts
      • Manual toggle capability and slash commands (/fissal raffle, /fr raffle)
]]--

FissalRelay = FissalRelay or {}
local FR = FissalRelay

local ESO_MAIL_BODY_LIMIT = 700

-- Default verified official data (matching Discord #raffle-announcements & message 908420248607260712)
local DEFAULT_RAFFLE_CACHE = {
    post = {
        guildKey = "post",
        guildLabel = "Redfur Trading Post",
        weekLabel = "Sep 6 - Sep 13",
        weekStart = 1788735600,
        pot = 1996000,
        tickets = 1996,
        entrants = 51,
        prizes = { first = 598800, second = 399200, third = 199600, guild = 798400 },
        winners = {
            { place = 1, name = "@albai06", ticket = 291, prize = 598800, entries = 2, tickets = 20, gold = 20000 },
            { place = 2, name = "@TaskiBeowolf", ticket = 1415, prize = 399200, entries = 2, tickets = 300, gold = 300000 },
            { place = 3, name = "@Utishta", ticket = 843, prize = 199600, entries = 1, tickets = 20, gold = 20000 },
        }
    },
    dealers = {
        guildKey = "dealers",
        guildLabel = "Redfur Dealers",
        weekLabel = "Sep 6 - Sep 13",
        weekStart = 1788735600,
        pot = 2920000,
        tickets = 2920,
        entrants = 35,
        prizes = { first = 876000, second = 584000, third = 292000, guild = 1168000 },
        winners = {
            { place = 1, name = "@Mysyic", ticket = 2364, prize = 876000, entries = 1, tickets = 1000, gold = 1000000 },
            { place = 2, name = "@warshepherd216", ticket = 15, prize = 584000, entries = 1, tickets = 50, gold = 50000 },
            { place = 3, name = "@RJ_Brown", ticket = 919, prize = 292000, entries = 1, tickets = 100, gold = 100000 },
        }
    }
}

--[[ =========================================================================
     DATA PROVIDER & PAYOUT STATE
========================================================================= ]]--

function FR:EnsureRaffleState()
    if not self.savedVars then return end
    if not self.savedVars.rafflePayouts then
        self.savedVars.rafflePayouts = {
            post = {},
            dealers = {},
        }
    end
    if not self.savedVars.raffleData then
        self.savedVars.raffleData = DEFAULT_RAFFLE_CACHE
    end
    if not self.savedVars.settings.raffleMail then
        self.savedVars.settings.raffleMail = {
            autoShowOnMail = true,
            soundEffects = true,
            sourceMode = "official",
            onlyShowIfPending = true,
        }
    end
    if self.savedVars.settings.raffleMail.onlyShowIfPending == nil then
        self.savedVars.settings.raffleMail.onlyShowIfPending = true
    end
    self.raffleSourceMode = self.savedVars.settings.raffleMail.sourceMode or "official"
end

-- Query available raffle weeks for navigation (Live, Discord Synced, and Archives)
function FR:GetRaffleWeeks(guildKey)
    self:EnsureRaffleState()
    local weeks = {}
    local seenKeys = {}

    -- 1. Live In-Game Addon Roll (if active in RaffleGold)
    if RaffleGold and RaffleGold.db and RaffleGold.db.prizes then
        local p = RaffleGold.db.prizes
        local rgGuild = RaffleGold.db.guild or ""
        local isPost = string.find(rgGuild, "Post") ~= nil
        local isDealers = string.find(rgGuild, "Dealer") ~= nil
        local targetMatches = (guildKey == "post" and isPost) or (guildKey == "dealers" and isDealers)
        if targetMatches and p.amtFrt and p.amtFrt > 0 and p.nameFrt then
            local label = isPost and "Redfur Trading Post (Local Roll)" or "Redfur Dealers (Local Roll)"
            local weekLbl = RaffleGold.db.dateStart and string.format("%s - %s", RaffleGold.db.dateStart, RaffleGold.db.dateEnd or "") or "Local Roll"
            table.insert(weeks, {
                guildKey = guildKey,
                guildLabel = label,
                weekLabel = weekLbl,
                weekKey = "local",
                weekStart = "local",
                isSynced = false,
                syncBadge = "|c00FFCC[LOCAL ROLL]|r",
                syncTooltip = "Live in-game roll read directly from RaffleGold.db SavedVariables.",
                pot = tonumber(p.tAmt) or 0,
                tickets = tonumber(p.eAmt) or 0,
                entrants = tonumber(RaffleGold.db.totalEntries) or 0,
                prizes = {
                    first = tonumber(p.amtFrt) or 0,
                    second = tonumber(p.amtScd) or 0,
                    third = tonumber(p.amtTrd) or 0,
                    guild = math.floor((tonumber(p.tAmt) or 0) * 0.4),
                },
                winners = {
                    { place = 1, name = p.nameFrt, ticket = tonumber(p.numFrt) or 0, prize = tonumber(p.amtFrt) or 0 },
                    { place = 2, name = p.nameScd, ticket = tonumber(p.numScd) or 0, prize = tonumber(p.amtScd) or 0 },
                    { place = 3, name = p.nameTrd, ticket = tonumber(p.numTrd) or 0, prize = tonumber(p.amtTrd) or 0 },
                }
            })
            seenKeys["local"] = true
        end
    end

    -- 2. Live Active Cycle (from current bank deposits & metrics)
    local guildId = self.ResolveGuildId and self:ResolveGuildId(guildKey)
    local dateInfo = self.GetRaffleDateInfo and self:GetRaffleDateInfo(guildId)
    local liveMetrics = (guildId and self.CalculateRaffleMetrics) and self:CalculateRaffleMetrics(guildId, 7, 1000)
    local curLabel = dateInfo and dateInfo.currentRange or "Active Cycle"
    if not seenKeys[curLabel] then
        local livePot = liveMetrics and liveMetrics.totalGold or 0
        local liveTickets = liveMetrics and liveMetrics.totalTickets or 0
        local liveEntrants = liveMetrics and liveMetrics.entrants or 0
        table.insert(weeks, {
            guildKey = guildKey,
            guildLabel = (guildKey == "post" and "Redfur Trading Post" or "Redfur Dealers"),
            weekLabel = curLabel .. " (Live)",
            weekKey = "live_" .. curLabel,
            weekStart = "live",
            isSynced = false,
            syncBadge = "|cFFD700[LIVE ACTIVE]|r",
            syncTooltip = "Live in-progress raffle cycle. Drawing scheduled for Sunday 7:00 PM ET.",
            pot = livePot,
            tickets = liveTickets,
            entrants = liveEntrants,
            prizes = {
                first = math.floor(livePot * 0.30),
                second = math.floor(livePot * 0.20),
                third = math.floor(livePot * 0.10),
                guild = math.floor(livePot * 0.40),
            },
            winners = {} -- Active cycle not yet drawn
        })
        seenKeys[curLabel] = true
    end

    -- 3. Official Sealed Draw Manifest (synced from Discord bot)
    if FR.OfficialRaffleLedger and FR.OfficialRaffleLedger[guildKey] then
        local off = FR.OfficialRaffleLedger[guildKey]
        local offKey = tostring(off.weekStart or off.weekLabel or "official")
        if not seenKeys[offKey] then
            table.insert(weeks, {
                guildKey = guildKey,
                guildLabel = off.guildLabel or (guildKey == "post" and "Redfur Trading Post" or "Redfur Dealers"),
                weekLabel = off.weekLabel or "Official Draw",
                weekKey = offKey,
                weekStart = off.weekStart,
                isSynced = true,
                syncBadge = "|c59E08A[SYNCED ✓ Discord]|r",
                syncTooltip = "Sealed and verified with #raffle-announcements on Discord.",
                pot = off.pot or 0,
                tickets = off.tickets or 0,
                entrants = off.entrants or 0,
                announceMessageId = off.announceMessageId,
                prizes = off.prizes or {},
                winners = off.winners or {},
            })
            seenKeys[offKey] = true
        end
    end

    -- 4. SavedVariables data
    if self.savedVars and self.savedVars.raffleData and self.savedVars.raffleData[guildKey] then
        local sv = self.savedVars.raffleData[guildKey]
        local svKey = tostring(sv.weekStart or sv.weekLabel or "saved")
        if not seenKeys[svKey] then
            table.insert(weeks, {
                guildKey = guildKey,
                guildLabel = sv.guildLabel or (guildKey == "post" and "Redfur Trading Post" or "Redfur Dealers"),
                weekLabel = sv.weekLabel or "Saved Draw",
                weekKey = svKey,
                weekStart = sv.weekStart,
                isSynced = true,
                syncBadge = "|c59E08A[SYNCED ✓ Discord]|r",
                syncTooltip = "Sealed draw synced from Discord relay.",
                pot = sv.pot or 0,
                tickets = sv.tickets or 0,
                entrants = sv.entrants or 0,
                announceMessageId = sv.announceMessageId,
                prizes = sv.prizes or {},
                winners = sv.winners or {},
            })
            seenKeys[svKey] = true
        end
    end

    -- 5. SavedVariables historical weeks
    if self.savedVars and self.savedVars.raffleHistory and self.savedVars.raffleHistory[guildKey] then
        for _, hist in ipairs(self.savedVars.raffleHistory[guildKey]) do
            local hKey = tostring(hist.weekStart or hist.weekLabel or "")
            if hKey ~= "" and not seenKeys[hKey] then
                table.insert(weeks, {
                    guildKey = guildKey,
                    guildLabel = hist.guildLabel or (guildKey == "post" and "Redfur Trading Post" or "Redfur Dealers"),
                    weekLabel = hist.weekLabel or "Archived Draw",
                    weekKey = hKey,
                    weekStart = hist.weekStart,
                    isSynced = true,
                    syncBadge = "|c00CCFF[SYNCED ✓ Archive]|r",
                    syncTooltip = "Historical archive stored in SavedVariables.",
                    pot = hist.pot or 0,
                    tickets = hist.tickets or 0,
                    entrants = hist.entrants or 0,
                    prizes = hist.prizes or {},
                    winners = hist.winners or {},
                })
                seenKeys[hKey] = true
            end
        end
    end

    -- 6. Default Verified Cache (Archive)
    if DEFAULT_RAFFLE_CACHE and DEFAULT_RAFFLE_CACHE[guildKey] then
        local def = DEFAULT_RAFFLE_CACHE[guildKey]
        local defKey = tostring(def.weekStart or def.weekLabel or "default")
        if not seenKeys[defKey] then
            table.insert(weeks, {
                guildKey = guildKey,
                guildLabel = def.guildLabel or (guildKey == "post" and "Redfur Trading Post" or "Redfur Dealers"),
                weekLabel = def.weekLabel or "Archive Draw",
                weekKey = defKey,
                weekStart = def.weekStart,
                isSynced = true,
                syncBadge = "|c00CCFF[SYNCED ✓ Archive]|r",
                syncTooltip = "Archived draw preserved in Fissal Relay ledger.",
                pot = def.pot or 0,
                tickets = def.tickets or 0,
                entrants = def.entrants or 0,
                prizes = def.prizes or {},
                winners = def.winners or {},
            })
            seenKeys[defKey] = true
        end
    end

    return weeks
end

-- Query active raffle data for a guild
function FR:GetRaffleData(guildKey)
    local weeks = self:GetRaffleWeeks(guildKey)
    if #weeks == 0 then
        return DEFAULT_RAFFLE_CACHE[guildKey]
    end

    local idx = self.currentRaffleWeekIdx or 1
    if idx > #weeks then idx = #weeks end
    if idx < 1 then idx = 1 end
    return weeks[idx] or DEFAULT_RAFFLE_CACHE[guildKey]
end

-- 1-Click Update MotD with Active Raffle Data
function FR:PushRaffleToMotD(guildKey)
    local guildId = self:ResolveGuildId(guildKey)
    if not guildId or guildId == 0 then
        self.PrintChat("|cFF5555Error:|r Could not resolve guild ID for " .. tostring(guildKey))
        return false
    end

    local guildName = GetGuildName(guildId)
    local hasPermission = DoesPlayerHaveGuildPermission and DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_SET_MOTD)
    local isGM = IsPlayerGuildMaster and IsPlayerGuildMaster(guildId)

    if not (isGM or hasPermission) then
        self.PrintChat(string.format("|cFF5555Permission Denied:|r You do not have permission to edit the MotD for %s.", guildName))
        if self.raffleMailStatusLabel then
            self.raffleMailStatusLabel:SetText(string.format("|cFF5555Permission Denied for %s|r", guildName))
        end
        return false
    end

    local currentMotD = GetGuildMotD(guildId) or ""

    -- Fable 5.1 Architecture: Retrieve persistent template for this guild rather than parsing live numbers
    local template = self.GetGuildMotDTemplate and self:GetGuildMotDTemplate(guildId)
    if not template or template == "" then
        -- Adopt live MotD if it still contains tokens
        if string.find(currentMotD, "{raffle_") ~= nil or string.find(currentMotD, "{drawing_date}") ~= nil then
            template = currentMotD
            if self.SetGuildMotDTemplate then self:SetGuildMotDTemplate(guildId, template) end
        end
    end

    if not template or template == "" then
        self.PrintChat(string.format("|cFF5555Error:|r No MotD template configured for %s! Type |cFF9900/fissal motd|r to open the MotD Broadcast Studio.", guildName))
        if self.raffleMailStatusLabel then
            self.raffleMailStatusLabel:SetText("|cFF5555Missing template - open /fissal motd|r")
        end
        return false
    end

    -- Interpolate persistent template with active raffle metrics
    local resolved = self:ResolveMotDTokens(template, guildId)

    -- Auto-balance unclosed color tags
    local _, colorStarts = string.gsub(resolved, "|c", "")
    local _, colorEnds = string.gsub(resolved, "|r", "")
    if colorStarts > colorEnds then
        resolved = resolved .. string.rep("|r", colorStarts - colorEnds)
    end

    local MAX_CHARS = MAX_GUILD_MOTD_LENGTH or 1024
    local charCount = (zo_strlen and zo_strlen(resolved)) or #resolved
    local byteCount = #resolved

    if charCount > MAX_CHARS or byteCount > MAX_CHARS then
        resolved = self:TruncateUtf8(resolved, MAX_CHARS)
        charCount = (zo_strlen and zo_strlen(resolved)) or #resolved
        byteCount = #resolved
    end

    -- Idempotency Guard (Fable 5.1 S2): Compare AFTER truncation against live MotD to prevent repeated churn
    if resolved == currentMotD then
        self.PrintChat(string.format("|c59E08A[MotD]|r %s Message of the Day is already up to date with active raffle data.", guildName))
        if self.raffleMailStatusLabel then
            self.raffleMailStatusLabel:SetText(string.format("|c59E08AMotD already up to date for %s!|r", guildName))
        end
        return true
    end

    SetGuildMotD(guildId, resolved)
    PlaySound(SOUNDS.GUILD_ROSTER_ADDED or SOUNDS.NOTE_SAVED)
    self.PrintChat(string.format("✓ |c59E08AMotD updated for %s!|r (%d chars, %d bytes)", guildName, charCount, byteCount))
    if self.raffleMailStatusLabel then
        self.raffleMailStatusLabel:SetText(string.format("|c59E08AMotD updated for %s!|r", guildName))
    end
    return true
end

-- Check if a specific winner payout has already been sent
function FR:IsPayoutSent(guildKey, weekKey, place)
    self:EnsureRaffleState()
    local payouts = self.savedVars.rafflePayouts
    if not payouts or not payouts[guildKey] then return false end

    local gPayouts = payouts[guildKey]
    local wKey = tostring(weekKey or "latest")
    if gPayouts[wKey] and gPayouts[wKey][place] and gPayouts[wKey][place].sent then
        return true, gPayouts[wKey][place]
    end
    return false, nil
end

-- Check if there are any winners across all guilds that have not yet been marked paid
function FR:HasPendingRafflePayouts()
    self:EnsureRaffleState()
    for _, gKey in ipairs({ "post", "dealers" }) do
        local data = self:GetRaffleData(gKey)
        if data and data.winners and #data.winners > 0 then
            local weekKey = data.weekStart or data.weekLabel or "latest"
            for i = 1, #data.winners do
                local isPaid = self:IsPayoutSent(gKey, weekKey, i)
                if not isPaid then
                    return true
                end
            end
        end
    end
    return false
end

-- Mark a winner as paid or unpaid
function FR:SetPayoutSent(guildKey, weekKey, place, sent, metadata)
    self:EnsureRaffleState()
    local payouts = self.savedVars.rafflePayouts
    if not payouts[guildKey] then payouts[guildKey] = {} end

    local wKey = tostring(weekKey or "latest")
    if not payouts[guildKey][wKey] then payouts[guildKey][wKey] = {} end

    if sent then
        payouts[guildKey][wKey][place] = {
            sent = true,
            sentAt = GetTimeStamp(),
            recipient = metadata and metadata.recipient or "",
            amount = metadata and metadata.amount or 0,
            ticket = metadata and metadata.ticket or 0,
        }
    else
        payouts[guildKey][wKey][place] = nil
    end

    if self.RefreshRaffleMailUI then
        self:RefreshRaffleMailUI()
    end
end

--[[ =========================================================================
     ESO MAIL FORMATTING
========================================================================= ]]--

local PLACE_NAMES = { "First", "Second", "Third" }

function FR:BuildMailSubject(guildLabel)
    local cleanLabel = string.gsub(guildLabel, "%s*%(Local Roll%)", "")
    return string.format("%s - Raffle Winner!", cleanLabel)
end

function FR:BuildMailBody(winner, guildLabel, placeIndex)
    local cleanLabel = string.gsub(guildLabel, "%s*%(Local Roll%)", "")
    local placeStr = PLACE_NAMES[placeIndex] or "Winning"
    local prizeStr = ZO_CommaDelimitNumber(winner.prize or 0)
    local ticketStr = winner.ticket and tostring(winner.ticket) or "--"
    local depStr = winner.gold and string.format("%sg", ZO_CommaDelimitNumber(winner.gold)) or ""
    local entriesStr = winner.entries and tostring(winner.entries) or "1"
    local tixStr = winner.tickets and tostring(winner.tickets) or ""

    local depLine = ""
    if winner.gold then
        depLine = string.format("\n|cB7C1C9Dep:|r |cFF7A2E%s|r  |cB7C1C9Ent:|r |c58E08A%s|r  |cB7C1C9Tix:|r |c58E08A%s|r", depStr, entriesStr, tixStr)
    end

    local body = string.format(
        "|t22:22:/esoui/art/currency/currency_eventticket.dds|t |cF2F6F8Weekly Raffle Winner|r\n" ..
        "|cB7C1C9%s|r\n\n" ..
        "|t22:22:/esoui/art/currency/currency_gold.dds|t |cF2F6F8Winner & Payout|r\n" ..
        "|cB7C1C9Fissal gives a pleased chirr, %s - your ticket landed |cFF7A2E%s|r.|r\n" ..
        "|cB7C1C9Prize:|r |cFF7A2E%sg|r  |cB7C1C9Ticket:|r |c58E08A#%s|r%s\n\n" ..
        "|cB7C1C9Fissal says: the ledger smiled on you this week.|r\n" ..
        "|cB7C1C9Results:|r https://redfur.ech-o.net\n\n" ..
        "|cFF521FRedfur|r |cB7C1C9and Fissal applaud your luck.|r",
        cleanLabel,
        winner.name,
        placeStr,
        prizeStr,
        ticketStr,
        depLine
    )

    if #body > ESO_MAIL_BODY_LIMIT then
        body = string.sub(body, 1, ESO_MAIL_BODY_LIMIT)
    end
    return body
end

--[[ =========================================================================
     AUTO-FILL EXECUTION
========================================================================= ]]--

function FR:AutoFillRaffleMail(guildKey, winner)
    if not winner or not winner.name or not winner.prize then return end

    local gData = self:GetRaffleData(guildKey)
    local guildLabel = gData.guildLabel or "Redfur Trading"
    local weekKey = gData.weekStart or gData.weekLabel or "latest"

    -- 1. Collision guard: ALWAYS clear existing money attachment first
    if QueueMoneyAttachment then
        QueueMoneyAttachment(0)
    end

    -- 2. Inventory check: ensure Echo has enough carried gold
    local carriedGold = GetCarriedCurrencyAmount(CURT_MONEY)
    if carriedGold < winner.prize then
        self.PrintChat(string.format(
            "|cFF5555[Warning]|r Carried gold (%sg) is less than prize (%sg)! Withdraw gold from bank first.",
            ZO_CommaDelimitNumber(carriedGold),
            ZO_CommaDelimitNumber(winner.prize)
        ))
        PlaySound(SOUNDS.GENERAL_ALERT_ERROR)
    end

    -- 3. Populate Native Mail Fields
    local subject = self:BuildMailSubject(guildLabel)
    local body = self:BuildMailBody(winner, guildLabel, winner.place)

    if ZO_MailSendToField then
        ZO_MailSendToField:SetText(winner.name)
    end

    if ZO_MailSendSubjectField then
        ZO_MailSendSubjectField:SetText(subject)
    end

    if ZO_MailSendBodyField then
        ZO_MailSendBodyField:SetText(body)
    end

    -- 4. Attach Prize Gold
    -- Ensure currency box edit session is terminated so focus/blue-border is cleared
    if ZO_MailSendSendCurrency and ZO_MailSendSendCurrency.OnEndInput then
        pcall(function() ZO_MailSendSendCurrency:OnEndInput() end)
    end

    if MAIL_SEND then
        if MAIL_SEND.SetMoneyAttachmentMode then
            pcall(function() MAIL_SEND:SetMoneyAttachmentMode() end)
        end
        if MAIL_SEND.AttachMoney then
            pcall(function() MAIL_SEND:AttachMoney(0, winner.prize) end)
        end
    end

    if QueueMoneyAttachment then
        QueueMoneyAttachment(winner.prize)
    end

    if ZO_MailSendBodyField then
        ZO_MailSendBodyField:TakeFocus()
    end

    -- 5. Set in-flight staging anchor for EVENT_MAIL_SEND_SUCCESS
    self.stagedRaffleWinner = {
        guild = guildKey,
        week = weekKey,
        place = winner.place,
        name = winner.name,
        prize = winner.prize,
        ticket = winner.ticket,
        stagedAt = GetTimeStamp(),
    }

    if self.raffleMailStatusLabel then
        self.raffleMailStatusLabel:SetText(string.format(
            "|c00FFCCStaged:|r |cFFFFFF%s|r (%s) • |cFF9900%sg|r",
            winner.name, PLACE_NAMES[winner.place] or "Winner", ZO_CommaDelimitNumber(winner.prize)
        ))
    end

    if self.savedVars.settings.raffleMail.soundEffects then
        PlaySound(SOUNDS.LOCKPICKING_UNLOCKED)
    end

    self.PrintChat(string.format(
        "Auto-filled mail for %s |c00FFCC%s|r (%sg). Ready to review and send!",
        PLACE_NAMES[winner.place] or "Winner", winner.name, ZO_CommaDelimitNumber(winner.prize)
    ))
end

--[[ =========================================================================
     EVENT HANDLERS: SEND SUCCESS & FAILURE
========================================================================= ]]--

local function OnMailSendSuccess()
    if not FR.stagedRaffleWinner then return end

    local staged = FR.stagedRaffleWinner
    local now = GetTimeStamp()

    -- Staging expiration: don't attribute if the player sent an unrelated mail hours later
    if staged.stagedAt and (now - staged.stagedAt > 180) then
        FR.stagedRaffleWinner = nil
        return
    end

    -- Idempotently mark as paid in SavedVariables
    FR:SetPayoutSent(staged.guild, staged.week, staged.place, true, {
        recipient = staged.name,
        amount = staged.prize,
        ticket = staged.ticket,
        sentAt = now,
    })

    FR.PrintChat(string.format(
        "|c58E08A[PAID]|r Recorded payout for |cFFFFFF%s|r (%s place, %sg). Ledger synchronized!",
        staged.name, PLACE_NAMES[staged.place] or "", ZO_CommaDelimitNumber(staged.prize)
    ))

    if FR.raffleMailStatusLabel then
        FR.raffleMailStatusLabel:SetText(string.format(
            "|c58E08APaid|r %s (%sg sent)", staged.name, ZO_CommaDelimitNumber(staged.prize)
        ))
    end

    FR.stagedRaffleWinner = nil

    if FR.RefreshRaffleMailUI then
        FR:RefreshRaffleMailUI()
    end

    PlaySound(SOUNDS.MAIL_SENT)
end

local function OnMailSendFailed()
    if FR.stagedRaffleWinner then
        FR.PrintChat("|cFF5555[Notice]|r Mail send canceled or failed. Payout not marked in ledger.")
        FR.stagedRaffleWinner = nil
    end
end

--[[ =========================================================================
     STAFF DISPATCH & MAIL STAGING HELPERS
========================================================================= ]]--

function FR:StageInactivityWarning(memberName, guildName, daysOffline)
    if not memberName or memberName == "" then return end
    guildName = guildName or "Redfur Trading Post"
    daysOffline = tonumber(daysOffline) or 14

    if SCENE_MANAGER and not SCENE_MANAGER:IsShowing("mailSend") then
        SCENE_MANAGER:Show("mailSend")
    end

    zo_callLater(function()
        if QueueMoneyAttachment then QueueMoneyAttachment(0) end
        if MAIL_SEND and MAIL_SEND.AttachMoney then
            pcall(function() MAIL_SEND:AttachMoney(0, 0) end)
        end

        local subject = string.format("[%s] Roster Check-in", guildName)
        local body = string.format(
            "|cF2F6F8Greetings %s,|r\n\n" ..
            "|cB7C1C9This is a friendly check-in from the staff of %s!|r\n" ..
            "|cB7C1C9We noticed you haven't been online in |cFF7A2E%d days|r.|r\n\n" ..
            "|cB7C1C9If you're taking a temporary break or need an LOA, please send a quick reply to let an officer know so we can safeguard your guild membership.|r\n\n" ..
            "|cFF521FRedfur|r |cB7C1C9wishes you safe travels!|r",
            memberName, guildName, daysOffline
        )

        if ZO_MailSendToField then ZO_MailSendToField:SetText(memberName) end
        if ZO_MailSendSubjectField then ZO_MailSendSubjectField:SetText(subject) end
        if ZO_MailSendBodyField then
            ZO_MailSendBodyField:SetText(body)
            ZO_MailSendBodyField:TakeFocus()
        end

        if self.raffleMailWindow then
            self.raffleMailWindow:SetHidden(false)
        end
        if self.raffleMailStatusText then
            self.raffleMailStatusText:SetText(string.format("|c59E08AStaged warning mail for %s (%d days offline)|r", memberName, daysOffline))
        end
    end, 150)
end

function FR:StageWelcomeLetter(memberName, guildName)
    if not memberName or memberName == "" then return end
    guildName = guildName or "Redfur Trading Post"

    if SCENE_MANAGER and not SCENE_MANAGER:IsShowing("mailSend") then
        SCENE_MANAGER:Show("mailSend")
    end

    zo_callLater(function()
        if QueueMoneyAttachment then QueueMoneyAttachment(0) end

        local subject = string.format("Welcome to %s!", guildName)
        local body = string.format(
            "|cF2F6F8Welcome to %s, %s!|r\n\n" ..
            "|cB7C1C9We are thrilled to have you in the family!|r\n\n" ..
            "|cFF7A2E* Guild Trader:|r |cB7C1C9Check guild MotD for our weekly trader location.|r\n" ..
            "|c58E08A* Weekly Raffle:|r |cB7C1C9Deposit 1,000g in guild bank for 1 ticket (drawn every Sunday).|r\n" ..
            "|c00E5FF* Discord Sanctuary:|r |cB7C1C9Join us for trial signups, events, and trade chat!|r\n\n" ..
            "|cB7C1C9If you have any questions, feel free to ask any officer!|r\n\n" ..
            "|cFF521FRedfur|r |cB7C1C9and Fissal bid you welcome!|r",
            guildName, memberName
        )

        if ZO_MailSendToField then ZO_MailSendToField:SetText(memberName) end
        if ZO_MailSendSubjectField then ZO_MailSendSubjectField:SetText(subject) end
        if ZO_MailSendBodyField then
            ZO_MailSendBodyField:SetText(body)
            ZO_MailSendBodyField:TakeFocus()
        end

        if self.raffleMailWindow then
            self.raffleMailWindow:SetHidden(false)
        end
        if self.raffleMailStatusText then
            self.raffleMailStatusText:SetText(string.format("|c59E08AStaged welcome mail for %s|r", memberName))
        end
    end, 150)
end

--[[ =========================================================================
     UI CONSTRUCTION & SCENE DOCKING
========================================================================= ]]--

function FR:CreateRaffleMailUI()
    if self.raffleMailWindow then return end

    local wm = WINDOW_MANAGER
    self.currentRaffleGuild = "post"
    self:EnsureRaffleState()

    -- 1. Main TopLevelWindow
    local win = wm:CreateTopLevelWindow("FissalRelay_RaffleMailWindow")
    win:SetDimensions(420, 490)
    win:SetClampedToScreen(true)
    win:SetMouseEnabled(true)
    win:SetMovable(true)

    -- Anchor restoration or default docking next to ZO_MailSend
    local pos = self.savedVars and self.savedVars.settings and self.savedVars.settings.raffleMailPos
    win:ClearAnchors()
    if pos and pos.x and pos.y and (pos.x ~= 0 or pos.y ~= 0) then
        win:SetAnchor(TOPLEFT, GuiRoot, TOPLEFT, pos.x, pos.y)
    elseif ZO_MailSend then
        win:SetAnchor(TOPRIGHT, ZO_MailSend, TOPLEFT, -12, 0)
    else
        win:SetAnchor(CENTER, GuiRoot, CENTER, -200, 0)
    end

    win:SetHandler("OnMoveStop", function(ctrl)
        if self.savedVars and self.savedVars.settings then
            self.savedVars.settings.raffleMailPos = {
                x = ctrl:GetLeft(),
                y = ctrl:GetTop(),
            }
        end
    end)

    -- 2. Dark Tinted Backdrop
    local backdrop = wm:CreateControl("$(parent)_Backdrop", win, CT_BACKDROP)
    backdrop:SetAnchorFill()
    backdrop:SetCenterColor(0.04, 0.04, 0.06, 0.94)
    backdrop:SetEdgeColor(0.75, 0.50, 0.10, 0.95)
    backdrop:SetEdgeTexture("", 8, 1, 0)

    -- 3. Clock Addon Status Meter Munge Texture
    local munge = wm:CreateControl("$(parent)_Munge", win, CT_TEXTURE)
    munge:SetAnchorFill()
    munge:SetTexture("EsoUI/Art/Performance/StatusMeterMunge.dds")
    munge:SetAlpha(0.65)

    -- 4. Header Icon & Title
    local icon = wm:CreateControl("$(parent)_Icon", win, CT_TEXTURE)
    icon:SetAnchor(TOPLEFT, win, TOPLEFT, 12, 10)
    icon:SetDimensions(22, 22)
    icon:SetTexture("/esoui/art/mainmenu/menubar_guilds_up.dds")

    local title = wm:CreateControl("$(parent)_Title", win, CT_LABEL)
    title:SetAnchor(LEFT, icon, RIGHT, 8, 0)
    title:SetFont("ZoFontGameBold")
    title:SetText("|cFF9900FISSAL|r |c00FFCCRAFFLE PAYOUTS|r")

    -- 5. Close Button [×]
    local closeBtn = wm:CreateControl("$(parent)_Close", win, CT_BUTTON)
    closeBtn:SetAnchor(TOPRIGHT, win, TOPRIGHT, -8, 8)
    closeBtn:SetDimensions(20, 20)
    closeBtn:SetFont("ZoFontGameBold")
    closeBtn:SetNormalFontColor(0.6, 0.6, 0.6, 1)
    closeBtn:SetMouseOverFontColor(1, 0.3, 0.3, 1)
    closeBtn:SetText("x")
    closeBtn:SetHandler("OnClicked", function()
        win:SetHidden(true)
    end)

    -- 6. Header Divider
    local divider = wm:CreateControl("$(parent)_Div1", win, CT_TEXTURE)
    divider:SetAnchor(TOPLEFT, win, TOPLEFT, 8, 36)
    divider:SetAnchor(TOPRIGHT, win, TOPRIGHT, -8, 36)
    divider:SetHeight(1)
    divider:SetColor(0.8, 0.5, 0.1, 0.4)

    -- 7. Guild Selector Tabs (Post vs Dealers)
    local tabPost = wm:CreateControl("$(parent)_TabPost", win, CT_BUTTON)
    tabPost:SetAnchor(TOPLEFT, win, TOPLEFT, 12, 44)
    tabPost:SetDimensions(195, 26)
    tabPost:SetFont("ZoFontGameBold")
    tabPost:SetText("Trading Post")

    local tabPostBg = wm:CreateControl("$(parent)_Bg", tabPost, CT_BACKDROP)
    tabPostBg:SetAnchorFill()
    tabPostBg:SetCenterColor(0.20, 0.12, 0.04, 0.90)
    tabPostBg:SetEdgeColor(0.90, 0.60, 0.10, 1.0)
    tabPostBg:SetEdgeTexture("", 1, 1, 0)

    local tabDealers = wm:CreateControl("$(parent)_TabDealers", win, CT_BUTTON)
    tabDealers:SetAnchor(TOPRIGHT, win, TOPRIGHT, -12, 44)
    tabDealers:SetDimensions(195, 26)
    tabDealers:SetFont("ZoFontGameBold")
    tabDealers:SetText("Dealers")

    local tabDealersBg = wm:CreateControl("$(parent)_Bg", tabDealers, CT_BACKDROP)
    tabDealersBg:SetAnchorFill()
    tabDealersBg:SetCenterColor(0.04, 0.04, 0.06, 0.60)
    tabDealersBg:SetEdgeColor(0.25, 0.25, 0.25, 0.50)
    tabDealersBg:SetEdgeTexture("", 1, 1, 0)

    local function UpdateTabs()
        if self.currentRaffleGuild == "post" then
            tabPostBg:SetCenterColor(0.20, 0.12, 0.04, 0.90)
            tabPostBg:SetEdgeColor(0.90, 0.60, 0.10, 1.0)
            tabPost:SetNormalFontColor(1, 0.8, 0.2, 1)

            tabDealersBg:SetCenterColor(0.04, 0.04, 0.06, 0.60)
            tabDealersBg:SetEdgeColor(0.25, 0.25, 0.25, 0.50)
            tabDealers:SetNormalFontColor(0.5, 0.5, 0.5, 1)
        else
            tabDealersBg:SetCenterColor(0.02, 0.16, 0.14, 0.90)
            tabDealersBg:SetEdgeColor(0, 0.90, 0.80, 1.0)
            tabDealers:SetNormalFontColor(0, 1, 0.8, 1)

            tabPostBg:SetCenterColor(0.04, 0.04, 0.06, 0.60)
            tabPostBg:SetEdgeColor(0.25, 0.25, 0.25, 0.50)
            tabPost:SetNormalFontColor(0.5, 0.5, 0.5, 1)
        end
    end

    tabPost:SetHandler("OnClicked", function()
        self.currentRaffleGuild = "post"
        self.currentRaffleWeekIdx = nil
        UpdateTabs()
        self:RefreshRaffleMailUI()
    end)

    tabDealers:SetHandler("OnClicked", function()
        self.currentRaffleGuild = "dealers"
        self.currentRaffleWeekIdx = nil
        UpdateTabs()
        self:RefreshRaffleMailUI()
    end)

    -- 8. Pot & Week Summary Banner
    local summaryBanner = wm:CreateControl("$(parent)_Summary", win, CT_CONTROL)
    summaryBanner:SetAnchor(TOPLEFT, win, TOPLEFT, 12, 76)
    summaryBanner:SetAnchor(TOPRIGHT, win, TOPRIGHT, -12, 76)
    summaryBanner:SetHeight(60)

    local sumBg = wm:CreateControl("$(parent)_Bg", summaryBanner, CT_BACKDROP)
    sumBg:SetAnchorFill()
    sumBg:SetCenterColor(0.06, 0.06, 0.10, 0.80)
    sumBg:SetEdgeColor(0.40, 0.30, 0.12, 0.65)
    sumBg:SetEdgeTexture("", 1, 1, 0)

    -- Week navigation controls: < (older) and > (newer)
    local prevWeekBtn = wm:CreateControl("$(parent)_PrevWeek", summaryBanner, CT_BUTTON)
    prevWeekBtn:SetAnchor(TOPLEFT, summaryBanner, TOPLEFT, 6, 6)
    prevWeekBtn:SetDimensions(20, 20)
    prevWeekBtn:SetFont("ZoFontGameBold")
    prevWeekBtn:SetText("<")
    prevWeekBtn:SetNormalFontColor(0.9, 0.7, 0.2, 1)
    prevWeekBtn:SetMouseOverFontColor(1, 0.9, 0.4, 1)
    prevWeekBtn:SetDisabledFontColor(0.35, 0.35, 0.35, 1)

    local sumWeekLbl = wm:CreateControl("$(parent)_Week", summaryBanner, CT_LABEL)
    sumWeekLbl:SetAnchor(LEFT, prevWeekBtn, RIGHT, 5, 0)
    sumWeekLbl:SetFont("ZoFontGameBold")
    sumWeekLbl:SetText("|c00FFCCWeek:|r |cFFFFFF--|r")

    local nextWeekBtn = wm:CreateControl("$(parent)_NextWeek", summaryBanner, CT_BUTTON)
    nextWeekBtn:SetAnchor(LEFT, sumWeekLbl, RIGHT, 5, 0)
    nextWeekBtn:SetDimensions(20, 20)
    nextWeekBtn:SetFont("ZoFontGameBold")
    nextWeekBtn:SetText(">")
    nextWeekBtn:SetNormalFontColor(0.9, 0.7, 0.2, 1)
    nextWeekBtn:SetMouseOverFontColor(1, 0.9, 0.4, 1)
    nextWeekBtn:SetDisabledFontColor(0.35, 0.35, 0.35, 1)

    local sumSyncLbl = wm:CreateControl("$(parent)_SyncBadge", summaryBanner, CT_LABEL)
    sumSyncLbl:SetAnchor(LEFT, nextWeekBtn, RIGHT, 6, 0)
    sumSyncLbl:SetFont("ZoFontGameSmall")
    sumSyncLbl:SetText("|c59E08A[SYNCED ✓]|r")
    sumSyncLbl:SetMouseEnabled(true)

    prevWeekBtn:SetHandler("OnClicked", function()
        local gKey = self.currentRaffleGuild or "post"
        local weeks = self:GetRaffleWeeks(gKey)
        self.currentRaffleWeekIdx = math.min((self.currentRaffleWeekIdx or 1) + 1, #weeks)
        self:RefreshRaffleMailUI()
    end)
    prevWeekBtn:SetHandler("OnMouseEnter", function(c)
        InitializeTooltip(InformationTooltip, c, TOP, 0, -4)
        SetTooltipText(InformationTooltip, "Step backward to prior raffle week archive.")
    end)
    prevWeekBtn:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)

    nextWeekBtn:SetHandler("OnClicked", function()
        self.currentRaffleWeekIdx = math.max((self.currentRaffleWeekIdx or 1) - 1, 1)
        self:RefreshRaffleMailUI()
    end)
    nextWeekBtn:SetHandler("OnMouseEnter", function(c)
        InitializeTooltip(InformationTooltip, c, TOP, 0, -4)
        SetTooltipText(InformationTooltip, "Step forward toward current live raffle week.")
    end)
    nextWeekBtn:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)

    -- Mode toggle button: Official Discord vs Local Addon Roll
    local sourceBtn = wm:CreateControl("$(parent)_SourceBtn", summaryBanner, CT_BUTTON)
    sourceBtn:SetAnchor(TOPRIGHT, summaryBanner, TOPRIGHT, -8, 6)
    sourceBtn:SetDimensions(116, 20)
    sourceBtn:SetFont("ZoFontGameSmall")
    sourceBtn:SetText("[Official Ledger]")

    local sourceBtnBg = wm:CreateControl("$(parent)_Bg", sourceBtn, CT_BACKDROP)
    sourceBtnBg:SetAnchorFill()
    sourceBtnBg:SetCenterColor(0.04, 0.04, 0.08, 0.80)
    sourceBtnBg:SetEdgeColor(0.65, 0.45, 0.10, 0.65)
    sourceBtnBg:SetEdgeTexture("", 1, 1, 0)

    local function UpdateSourceBtn()
        if self.raffleSourceMode == "local" then
            sourceBtn:SetText("|c00FFCC[Local Addon Roll]|r")
            sourceBtnBg:SetEdgeColor(0, 0.85, 0.75, 0.8)
        else
            sourceBtn:SetText("|cFFD700[Official Ledger]|r")
            sourceBtnBg:SetEdgeColor(0.85, 0.60, 0.10, 0.8)
        end
    end

    sourceBtn:SetHandler("OnClicked", function()
        if self.raffleSourceMode == "local" then
            self.raffleSourceMode = "official"
        else
            self.raffleSourceMode = "local"
        end
        if self.savedVars and self.savedVars.settings and self.savedVars.settings.raffleMail then
            self.savedVars.settings.raffleMail.sourceMode = self.raffleSourceMode
        end
        UpdateSourceBtn()
        self:RefreshRaffleMailUI()
        self.PrintChat(string.format("Switched raffle data source to: %s",
            self.raffleSourceMode == "local" and "|c00FFCCLocal Addon Roll|r" or "|cFFD700Official Ledger|r"))
    end)
    sourceBtn:SetHandler("OnMouseEnter", function(c)
        InitializeTooltip(InformationTooltip, c, TOP, 0, -4)
        SetTooltipText(InformationTooltip, "Toggle between:\n• Official Ledger: Synchronized with #raffle-announcements & bot database.\n• Local Addon Roll: Reads live RaffleGold in-game SavedVariables.")
    end)
    sourceBtn:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)

    -- Row 2: Pot, Tickets, and 1-Click Update MotD Button
    local sumPotLbl = wm:CreateControl("$(parent)_Pot", summaryBanner, CT_LABEL)
    sumPotLbl:SetAnchor(TOPLEFT, summaryBanner, TOPLEFT, 8, 34)
    sumPotLbl:SetFont("ZoFontGameSmall")
    sumPotLbl:SetText("Pot: --")

    local updateMotdBtn = wm:CreateControl("$(parent)_UpdateMotdBtn", summaryBanner, CT_BUTTON)
    updateMotdBtn:SetAnchor(TOPRIGHT, summaryBanner, TOPRIGHT, -8, 32)
    updateMotdBtn:SetDimensions(95, 22)
    updateMotdBtn:SetFont("ZoFontGameSmall")
    updateMotdBtn:SetText("|cFFD700Update MotD|r")

    local motdBg = wm:CreateControl("$(parent)_Bg", updateMotdBtn, CT_BACKDROP)
    motdBg:SetAnchorFill()
    motdBg:SetCenterColor(0.18, 0.10, 0.02, 0.90)
    motdBg:SetEdgeColor(0.95, 0.65, 0.15, 0.90)
    motdBg:SetEdgeTexture("", 1, 1, 0)

    updateMotdBtn:SetHandler("OnMouseEnter", function()
        motdBg:SetCenterColor(0.28, 0.16, 0.04, 1.0)
        motdBg:SetEdgeColor(1.0, 0.85, 0.3, 1.0)
        InitializeTooltip(InformationTooltip, updateMotdBtn, TOP, 0, -4)
        SetTooltipText(InformationTooltip, "Interpolate this guild's MotD with active raffle data (pot, tickets, entrants, dates).\nRequires MotD edit permissions and valid template tokens or raffle fields.")
    end)
    updateMotdBtn:SetHandler("OnMouseExit", function()
        motdBg:SetCenterColor(0.18, 0.10, 0.02, 0.90)
        motdBg:SetEdgeColor(0.95, 0.65, 0.15, 0.90)
        ClearTooltip(InformationTooltip)
    end)
    updateMotdBtn:SetHandler("OnClicked", function()
        self:PushRaffleToMotD(self.currentRaffleGuild or "post")
    end)

    local sumTicketsLbl = wm:CreateControl("$(parent)_Tickets", summaryBanner, CT_LABEL)
    sumTicketsLbl:SetAnchor(RIGHT, updateMotdBtn, LEFT, -10, 0)
    sumTicketsLbl:SetFont("ZoFontGameSmall")
    sumTicketsLbl:SetText("Tickets: --")

    self.raffleMailWeekLbl = sumWeekLbl
    self.raffleMailPotLbl = sumPotLbl
    self.raffleMailTicketsLbl = sumTicketsLbl
    self.raffleMailSyncLbl = sumSyncLbl
    self.raffleMailPrevWeekBtn = prevWeekBtn
    self.raffleMailNextWeekBtn = nextWeekBtn
    self.raffleMailSourceBtn = sourceBtn
    UpdateSourceBtn()

    -- 9. Winner Cards (1st, 2nd, 3rd)
    self.winnerControls = {}
    local rankTitles = {
        "|t18:18:/esoui/art/compass/groupleader.dds|t |cFFD7001st Place|r",
        "|cCCCCCC[2] 2nd Place|r",
        "|cCD7F32[3] 3rd Place|r",
    }
    local rankBorderColors = {
        { 0.75, 0.55, 0.10, 0.75 }, -- Gold
        { 0.60, 0.60, 0.65, 0.60 }, -- Silver
        { 0.65, 0.45, 0.25, 0.60 }, -- Bronze
    }

    for i = 1, 3 do
        local card = wm:CreateControl("$(parent)_WinnerCard" .. i, win, CT_CONTROL)
        card:SetAnchor(TOPLEFT, win, TOPLEFT, 12, 142 + (i - 1) * 88)
        card:SetAnchor(TOPRIGHT, win, TOPRIGHT, -12, 142 + (i - 1) * 88)
        card:SetHeight(82)

        local cardBg = wm:CreateControl("$(parent)_Bg", card, CT_BACKDROP)
        cardBg:SetAnchorFill()
        cardBg:SetCenterColor(0.04, 0.04, 0.07, 0.88)
        cardBg:SetEdgeColor(unpack(rankBorderColors[i]))
        cardBg:SetEdgeTexture("", 1, 1, 0)

        local cardRank = wm:CreateControl("$(parent)_Rank", card, CT_LABEL)
        cardRank:SetAnchor(TOPLEFT, card, TOPLEFT, 10, 8)
        cardRank:SetFont("ZoFontGameBold")
        cardRank:SetText(rankTitles[i])

        local cardStatus = wm:CreateControl("$(parent)_Status", card, CT_BUTTON)
        cardStatus:SetAnchor(TOPRIGHT, card, TOPRIGHT, -10, 8)
        cardStatus:SetDimensions(90, 20)
        cardStatus:SetFont("ZoFontGameBold")
        cardStatus:SetText("|cFF9900[PENDING]|r")

        local cardName = wm:CreateControl("$(parent)_Name", card, CT_LABEL)
        cardName:SetAnchor(TOPLEFT, cardRank, BOTTOMLEFT, 0, 3)
        cardName:SetFont("ZoFontGameBold")
        cardName:SetText("|cFFFFFF@username|r")

        local cardDetails = wm:CreateControl("$(parent)_Details", card, CT_LABEL)
        cardDetails:SetAnchor(TOPLEFT, cardName, BOTTOMLEFT, 0, 2)
        cardDetails:SetFont("ZoFontGameSmall")
        cardDetails:SetColor(0.75, 0.75, 0.75, 1)
        cardDetails:SetText("Ticket #-- | Wins --g")

        local fillBtn = wm:CreateControl("$(parent)_FillBtn", card, CT_BUTTON)
        fillBtn:SetAnchor(BOTTOMRIGHT, card, BOTTOMRIGHT, -10, -8)
        fillBtn:SetDimensions(116, 24)
        fillBtn:SetFont("ZoFontGame")
        fillBtn:SetText("Auto-Fill Mail")

        local fillBg = wm:CreateControl("$(parent)_Bg", fillBtn, CT_BACKDROP)
        fillBg:SetAnchorFill()
        fillBg:SetCenterColor(0.08, 0.16, 0.20, 0.85)
        fillBg:SetEdgeColor(0, 0.8, 0.7, 0.8)
        fillBg:SetEdgeTexture("", 1, 1, 0)

        fillBtn:SetHandler("OnMouseEnter", function()
            fillBg:SetCenterColor(0.12, 0.24, 0.30, 0.95)
            fillBg:SetEdgeColor(0.2, 1.0, 0.9, 1.0)
        end)
        fillBtn:SetHandler("OnMouseExit", function()
            FR:RefreshRaffleMailUI()
        end)

        self.winnerControls[i] = {
            card = card,
            rank = cardRank,
            name = cardName,
            details = cardDetails,
            status = cardStatus,
            fillBtn = fillBtn,
            fillBg = fillBg,
            place = i,
        }
    end

    -- 10. Footer Status & Controls
    local footerDiv = wm:CreateControl("$(parent)_FooterDiv", win, CT_TEXTURE)
    footerDiv:SetAnchor(BOTTOMLEFT, win, BOTTOMLEFT, 8, -44)
    footerDiv:SetAnchor(BOTTOMRIGHT, win, BOTTOMRIGHT, -8, -44)
    footerDiv:SetHeight(1)
    footerDiv:SetColor(0.8, 0.5, 0.1, 0.3)

    local statusLbl = wm:CreateControl("$(parent)_StatusText", win, CT_LABEL)
    statusLbl:SetAnchor(BOTTOMLEFT, win, BOTTOMLEFT, 12, -20)
    statusLbl:SetAnchor(BOTTOMRIGHT, win, BOTTOMRIGHT, -90, -20)
    statusLbl:SetFont("ZoFontGameSmall")
    statusLbl:SetColor(0.8, 0.8, 0.8, 1)
    statusLbl:SetText("|c00FF00[ON]|r Ready | Auto-Attaches Gold | /fr")
    self.raffleMailStatusLabel = statusLbl

    local clearBtn = wm:CreateControl("$(parent)_ClearBtn", win, CT_BUTTON)
    clearBtn:SetAnchor(BOTTOMRIGHT, win, BOTTOMRIGHT, -12, -18)
    clearBtn:SetDimensions(74, 20)
    clearBtn:SetFont("ZoFontGameSmall")
    clearBtn:SetNormalFontColor(0.8, 0.3, 0.3, 1)
    clearBtn:SetMouseOverFontColor(1, 0.5, 0.5, 1)
    clearBtn:SetText("[Clear]")
    clearBtn:SetHandler("OnClicked", function()
        if QueueMoneyAttachment then QueueMoneyAttachment(0) end
        if ZO_MailSendToField then ZO_MailSendToField:SetText("") end
        if ZO_MailSendSubjectField then ZO_MailSendSubjectField:SetText("") end
        if ZO_MailSendBodyField then ZO_MailSendBodyField:SetText("") end
        self.stagedRaffleWinner = nil
        statusLbl:SetText("|c00FF00[ON]|r Form cleared | /fr")
    end)

    self.raffleMailWindow = win
    UpdateTabs()

    -- 11. Docking Scene Fragment
    local mailScene = SCENE_MANAGER:GetScene("mailSend")
    if mailScene then
        self.raffleMailFragment = ZO_SimpleSceneFragment:New(win)
        mailScene:AddFragment(self.raffleMailFragment)

        mailScene:RegisterCallback("StateChange", function(oldState, newState)
            if newState == SCENE_SHOWN then
                local shouldShow = true
                local rmSettings = self.savedVars and self.savedVars.settings and self.savedVars.settings.raffleMail
                if rmSettings then
                    if rmSettings.autoShowOnMail == false then
                        shouldShow = false
                    elseif rmSettings.onlyShowIfPending ~= false then
                        shouldShow = self:HasPendingRafflePayouts()
                    end
                end
                if shouldShow then
                    win:SetHidden(false)
                    self:RefreshRaffleMailUI()
                else
                    win:SetHidden(true)
                end
            elseif newState == SCENE_HIDING then
                -- Keep stagedRaffleWinner intact so EVENT_MAIL_SEND_SUCCESS can record payout
            end
        end)
    end

    self:RefreshRaffleMailUI()
end

-- Refresh UI contents for currently selected guild
function FR:RefreshRaffleMailUI()
    if not self.raffleMailWindow then return end

    local gKey = self.currentRaffleGuild or "post"
    local weeks = self:GetRaffleWeeks(gKey)

    -- Default to the most recent week that has winners, or 1
    if not self.currentRaffleWeekIdx then
        self.currentRaffleWeekIdx = 1
        for i, w in ipairs(weeks) do
            if w.winners and #w.winners > 0 then
                self.currentRaffleWeekIdx = i
                break
            end
        end
    end

    if self.currentRaffleWeekIdx > #weeks then self.currentRaffleWeekIdx = #weeks end
    if self.currentRaffleWeekIdx < 1 then self.currentRaffleWeekIdx = 1 end

    local data = weeks[self.currentRaffleWeekIdx] or DEFAULT_RAFFLE_CACHE[gKey]
    local weekKey = data.weekStart or data.weekLabel or "latest"

    if self.raffleMailWeekLbl then
        self.raffleMailWeekLbl:SetText(string.format("|c00FFCCWeek:|r |cFFFFFF%s|r", data.weekLabel or "--"))
    end

    if self.raffleMailSyncLbl then
        self.raffleMailSyncLbl:SetText(data.syncBadge or "")
        self.raffleMailSyncLbl:SetHandler("OnMouseEnter", function(c)
            InitializeTooltip(InformationTooltip, c, TOP, 0, -4)
            SetTooltipText(InformationTooltip, data.syncTooltip or "Raffle week status.")
        end)
        self.raffleMailSyncLbl:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)
    end

    if self.raffleMailPrevWeekBtn then
        self.raffleMailPrevWeekBtn:SetEnabled(self.currentRaffleWeekIdx < #weeks)
    end
    if self.raffleMailNextWeekBtn then
        self.raffleMailNextWeekBtn:SetEnabled(self.currentRaffleWeekIdx > 1)
    end

    if self.raffleMailPotLbl then
        local potStr = ZO_CommaDelimitNumber(data.pot or 0)
        local payStr = data.prizes and ZO_CommaDelimitNumber((data.prizes.first or 0) + (data.prizes.second or 0) + (data.prizes.third or 0)) or "--"
        self.raffleMailPotLbl:SetText(string.format("Pot: |cFFFFFF%s|r |t12:12:/esoui/art/currency/currency_gold.dds|t  |  Payout: |cFF7A2E%s|r |t12:12:/esoui/art/currency/currency_gold.dds|t", potStr, payStr))
    end

    if self.raffleMailTicketsLbl then
        local tixStr = ZO_CommaDelimitNumber(data.tickets or 0)
        self.raffleMailTicketsLbl:SetText(string.format("Tickets: |c58E08A%s|r", tixStr))
    end

    local rankTitles = {
        "|t18:18:/esoui/art/compass/groupleader.dds|t |cFFD7001st Place|r",
        "|cCCCCCC[2] 2nd Place|r",
        "|cCD7F32[3] 3rd Place|r",
    }

    local winners = data.winners or {}
    if #winners == 0 then
        -- Empty state: Live cycle in progress before Sunday draw
        local ctrl1 = self.winnerControls[1]
        if ctrl1 then
            ctrl1.card:SetHidden(false)
            ctrl1.rank:SetText("|cFFD700★ Active Cycle In Progress|r")
            ctrl1.name:SetText("|cFFFFFFDrawing Pending|r")
            ctrl1.details:SetText("Live bank deposits accumulating. Official draw scheduled for Sunday 7:00 PM ET.")
            ctrl1.status:SetText("|c888888[IN PROGRESS]|r")
            ctrl1.status:SetHandler("OnClicked", nil)
            ctrl1.fillBtn:SetText("|c555555Awaiting Draw|r")
            ctrl1.fillBtn:SetEnabled(false)
            if ctrl1.fillBg then
                ctrl1.fillBg:SetCenterColor(0.04, 0.04, 0.06, 0.5)
                ctrl1.fillBg:SetEdgeColor(0.3, 0.3, 0.3, 0.4)
            end
        end
        for i = 2, 3 do
            if self.winnerControls[i] then
                self.winnerControls[i].card:SetHidden(true)
            end
        end
    else
        for i = 1, 3 do
            local ctrl = self.winnerControls[i]
            local w = winners[i]

            if ctrl and w then
                ctrl.card:SetHidden(false)
                ctrl.rank:SetText(rankTitles[i] or ("#" .. i))
                ctrl.name:SetText(string.format("|cFFFFFF%s|r", w.name))
                ctrl.fillBtn:SetEnabled(true)

                local prizeStr = ZO_CommaDelimitNumber(w.prize or 0)
                local ticketStr = w.ticket and tostring(w.ticket) or "--"
                ctrl.details:SetText(string.format("Ticket #|cFFFFFF%s|r  |  Prize: |cFFD700%s|r |t13:13:EsoUI/Art/currency/currency_gold.dds|t", ticketStr, prizeStr))

                local isPaid, pInfo = self:IsPayoutSent(gKey, weekKey, i)

                if isPaid then
                    ctrl.status:SetText("|c58E08A[PAID]|r")
                    ctrl.fillBtn:SetText("|c888888Paid|r")
                    if ctrl.fillBg then
                        ctrl.fillBg:SetCenterColor(0.04, 0.07, 0.05, 0.6)
                        ctrl.fillBg:SetEdgeColor(0.2, 0.45, 0.25, 0.5)
                    end
                else
                    ctrl.status:SetText("|cFF9900[PENDING]|r")
                    ctrl.fillBtn:SetText("|c00FFCCAuto-Fill Mail|r")
                    if ctrl.fillBg then
                        ctrl.fillBg:SetCenterColor(0.08, 0.16, 0.20, 0.85)
                        ctrl.fillBg:SetEdgeColor(0, 0.8, 0.7, 0.8)
                    end
                end

            -- Allow clicking status label to toggle paid state manually
            ctrl.status:SetHandler("OnClicked", function()
                local newSent = not isPaid
                self:SetPayoutSent(gKey, weekKey, i, newSent, {
                    recipient = w.name,
                    amount = w.prize,
                    ticket = w.ticket,
                    manual = true,
                    timestamp = GetTimeStamp(),
                })
                self.PrintChat(string.format("Toggled %s %s place payout status to: %s", gKey, PLACE_NAMES[i] or "", newSent and "|c58E08APAID|r" or "|cFF9900PENDING|r"))
                self:RefreshRaffleMailUI()
            end)
            ctrl.status:SetHandler("OnMouseEnter", function(c)
                InitializeTooltip(InformationTooltip, c, TOP, 0, -4)
                SetTooltipText(InformationTooltip, "Click to manually toggle Paid / Pending status in ledger.")
            end)
            ctrl.status:SetHandler("OnMouseExit", function() ClearTooltip(InformationTooltip) end)

            -- Auto-Fill button handler
            ctrl.fillBtn:SetHandler("OnClicked", function()
                if isPaid then
                    self.PrintChat(string.format("|cFF5555[Notice]|r %s is already marked PAID. Auto-filling anyway...", w.name))
                end
                self:AutoFillRaffleMail(gKey, w)
            end)
        elseif ctrl then
            ctrl.card:SetHidden(true)
        end
    end
end
end

function FR:ToggleRaffleMailUI(show)
    if not self.raffleMailWindow then
        self:CreateRaffleMailUI()
    end
    if show == nil then
        self.raffleMailWindow:SetHidden(not self.raffleMailWindow:IsHidden())
    else
        self.raffleMailWindow:SetHidden(not show)
    end
    if not self.raffleMailWindow:IsHidden() then
        self:RefreshRaffleMailUI()
    end
end

--[[ =========================================================================
     INITIALIZATION & EVENT REGISTRATION
========================================================================= ]]--

local function OnPlayerActivated()
    EVENT_MANAGER:UnregisterForEvent("FissalRelay_RaffleMail_Init", EVENT_PLAYER_ACTIVATED)
    FR:EnsureRaffleState()
    FR:CreateRaffleMailUI()
end

EVENT_MANAGER:RegisterForEvent("FissalRelay_RaffleMail_Init", EVENT_PLAYER_ACTIVATED, OnPlayerActivated)
EVENT_MANAGER:RegisterForEvent("FissalRelay_RaffleMail", EVENT_MAIL_SEND_SUCCESS, OnMailSendSuccess)
EVENT_MANAGER:RegisterForEvent("FissalRelay_RaffleMail", EVENT_MAIL_SEND_FAILED, OnMailSendFailed)

-- Register slash commands
SLASH_COMMANDS["/fissalraffle"] = function() FR:ToggleRaffleMailUI() end
SLASH_COMMANDS["/frraffle"] = function() FR:ToggleRaffleMailUI() end
