--[[
    FissalRelay_Ranks.lua
    Auto-Ranker & Roster Automation Engine for Fissal's Cogwork Relay
    Crafted by Echo & Fissal for Fissal Relay and the Redfur Guilds.

    Features:
      • Multi-Tier Customizable Rank Dues Leaderboard (matching legacy AutoRanks)
      • Per-guild rank ladder configuration with customizable sales and bank dues thresholds
      • Smart Redfur defaults for Dealers (25k), Post (1 sale / 1k deposit), Caravan (1 sale)
      • Interactive [⚙ Configure Rank Dues] setup drawer with steppers and presets
      • Dynamic top-to-bottom ladder evaluation engine
      • High-safety guards:
          - Officer Immunity (GM and Officer permissions protected)
          - DNR & PERM Note Shield (skips members whose note contains 'DNR', 'PERM', 'VIP', 'CORE')
          - New Member Probation Grace Period (protects recruits within 7-day grace)
          - Self & Higher-Rank Protection (never attempts to alter self or superior ranks)
          - Demotion Step Cap & Restrict Demotions safety modes
      • Modernized scrollable list with mouse wheel support, pagination, and clean icons
      • Rich hover tooltips detailing sales volume, bank deposits, notes, and exact threshold qualification
      • Paced, ack-gated batch execution engine with live re-validation and emergency abort
      • Full slash command routing (/fissal ranks, /fr ranks, /ar)
]]--

FissalRelay = FissalRelay or {}
local FR = FissalRelay

local ROWS_PER_PAGE = 13

FR.autoRankResults = {}
FR.autoRankFilteredResults = {}
FR.autoRankCurrentPage = 1
FR.autoRankTasks = {}
FR.autoRankBatchRunning = false
FR.autoRankFilter = "all" -- "all", "changes", "promote", "demote", "exempt"
FR.autoRankLookbackDays = 10

-- Standard preset stepper values
local SALES_STEPS = { 0, 1000, 5000, 10000, 25000, 50000, 100000, 250000, 500000, 1000000 }
local COUNT_STEPS = { 0, 1, 2, 3, 5, 10, 20, 50, 100 }
local DUES_STEPS = { 0, 8, 500, 1000, 2500, 5000, 10000, 20000, 25000, 50000, 100000 }
local MODES = { "OR", "SUM", "AND" }

--[[ =========================================================================
     STATE & CONFIGURATION
========================================================================= ]]--

function FR:EnsureAutoRankState()
    if not self.savedVars then return end
    if not self.savedVars.autoRanks then
        self.savedVars.autoRanks = {
            officerImmunity = true,
            protectDNR = true,
            probationDays = 7,
            demoteCap = 1,
            restrictDemotions = false,
            lookbackDays = 10,
            guildConfigs = {},
        }
    end
    local ar = self.savedVars.autoRanks
    if ar.officerImmunity == nil then ar.officerImmunity = true end
    if ar.protectDNR == nil then ar.protectDNR = true end
    if ar.probationDays == nil then ar.probationDays = 7 end
    if ar.demoteCap == nil then ar.demoteCap = 1 end
    if ar.restrictDemotions == nil then ar.restrictDemotions = false end
    if ar.lookbackDays == nil then ar.lookbackDays = 10 end
    if ar.guildConfigs == nil then ar.guildConfigs = {} end
    self.autoRankLookbackDays = ar.lookbackDays
end

-- Generates default rank ladder thresholds based on guild identity & rank structure
function FR:GetDefaultRankConfig(guildId)
    local numRanks = GetNumGuildRanks(guildId)
    local gName = string.lower(GetGuildName(guildId) or "")
    local isDealers = string.find(gName, "dealer") ~= nil
    local isPost = string.find(gName, "post") ~= nil
    local isCaravan = string.find(gName, "caravan") ~= nil

    local ranks = {}
    for r = 1, numRanks do
        local rName = GetFinalGuildRankName(guildId, r) or ("Rank " .. r)
        local isOfficer, offReason = self:IsOfficerCapableRank(guildId, r)

        if isOfficer or r <= 2 then
            ranks[r] = {
                name = rName,
                isOfficer = true,
                minSales = 0,
                minDues = 0,
                mode = "OR",
            }
        else
            -- Member tier defaults
            local minSales = 0
            local minDues = 0
            local mode = "OR"

            if isDealers then
                if r == 3 then
                    minSales = 100000
                    minDues = 50000
                    mode = "OR"
                elseif r == 4 or r == (numRanks - 1) then
                    minSales = 25000
                    minDues = 25000
                    mode = "SUM" -- 25k combined sales + deposit
                else
                    minSales = 0
                    minDues = 0
                    mode = "OR"
                end
            elseif isPost then
                if r == 3 then
                    minSales = 250000
                    minDues = 25000
                    mode = "OR"
                elseif r == 4 or r == (numRanks - 1) then
                    minSales = 1 -- 1 sale
                    minDues = 1000 -- or 1k deposit
                    mode = "OR"
                else
                    minSales = 0
                    minDues = 0
                    mode = "OR"
                end
            elseif isCaravan then
                if r == 3 then
                    minSales = 100000
                    minDues = 0
                    mode = "OR"
                elseif r == 4 or r == (numRanks - 1) then
                    minSales = 1 -- 1 sale
                    minDues = 0
                    mode = "OR"
                else
                    minSales = 0
                    minDues = 0
                    mode = "OR"
                end
            else
                -- Generic guild ladder
                if r == 3 then
                    minSales = 100000
                    minDues = 20000
                    mode = "OR"
                elseif r == 4 then
                    minSales = 25000
                    minDues = 5000
                    mode = "OR"
                elseif r == (numRanks - 1) then
                    minSales = 1
                    minDues = 1000
                    mode = "OR"
                else
                    minSales = 0
                    minDues = 0
                    mode = "OR"
                end
            end

            ranks[r] = {
                name = rName,
                isOfficer = false,
                isIgnored = false,
                minSales = minSales,
                minSalesCount = (minSales == 1) and 1 or 0,
                minDues = minDues,
                mode = mode,
            }
        end
    end

    return { ranks = ranks }
end

function FR:GetGuildRankConfig(guildId)
    self:EnsureAutoRankState()
    local gKey = tostring(guildId)
    local cfg = self.savedVars.autoRanks.guildConfigs[gKey]

    if not cfg or not cfg.ranks then
        cfg = self:GetDefaultRankConfig(guildId)
        self.savedVars.autoRanks.guildConfigs[gKey] = cfg
    else
        -- Re-sync rank count in case guild ranks were added or removed
        local numRanks = GetNumGuildRanks(guildId)
        for r = 1, numRanks do
            if not cfg.ranks[r] then
                local rName = GetFinalGuildRankName(guildId, r) or ("Rank " .. r)
                local isOfficer = self:IsOfficerCapableRank(guildId, r) or (r <= 2)
                cfg.ranks[r] = {
                    name = rName,
                    isOfficer = isOfficer,
                    isIgnored = false,
                    minSales = 0,
                    minSalesCount = 0,
                    minDues = 0,
                    mode = "OR",
                }
            else
                local rCfg = cfg.ranks[r]
                if rCfg.isIgnored == nil then rCfg.isIgnored = false end
                if rCfg.minSalesCount == nil then
                    if rCfg.minSales == 1 then
                        rCfg.minSalesCount = 1
                        rCfg.minSales = 0
                    else
                        rCfg.minSalesCount = 0
                    end
                end
            end
        end
    end

    return cfg
end

--[[ =========================================================================
     OFFICER & PERMISSION IMMUNITY GUARDS
========================================================================= ]]--

function FR:IsOfficerCapableRank(guildId, rankIndex)
    if not guildId or not rankIndex then return false end
    if IsGuildRankGuildMaster and IsGuildRankGuildMaster(guildId, rankIndex) then
        return true, "Guild Master"
    end
    if DoesGuildRankHavePermission then
        local officerPerms = {
            { perm = GUILD_PERMISSION_PROMOTE, name = "Promote" },
            { perm = GUILD_PERMISSION_DEMOTE, name = "Demote" },
            { perm = GUILD_PERMISSION_REMOVE, name = "Remove / Kick" },
            { perm = GUILD_PERMISSION_SET_MOTD, name = "Set MotD" },
        }
        if GUILD_PERMISSION_CLAIM_KIOSK then
            table.insert(officerPerms, { perm = GUILD_PERMISSION_CLAIM_KIOSK, name = "Claim Kiosk" })
        end
        if GUILD_PERMISSION_DESCRIPTION_EDIT then
            table.insert(officerPerms, { perm = GUILD_PERMISSION_DESCRIPTION_EDIT, name = "Edit Description" })
        end

        for _, entry in ipairs(officerPerms) do
            if entry.perm and DoesGuildRankHavePermission(guildId, rankIndex, entry.perm) then
                return true, entry.name
            end
        end
    end
    -- In ESO, rank 1 is always Guild Master
    if rankIndex == 1 then return true, "Rank 1 (GM)" end
    return false
end

--[[ =========================================================================
     EVALUATION ENGINE (LADDER WALK)
========================================================================= ]]--

function FR:EvaluateAutoRanks(guildId)
    self:EnsureAutoRankState()
    guildId = self:ResolveGuildId(guildId or self.selectedGuildIndex or 1)
    if not guildId or guildId == 0 then return {} end

    local guildName = GetGuildName(guildId)
    local numMembers = GetNumGuildMembers(guildId)
    local numRanks = GetNumGuildRanks(guildId)
    local lookbackDays = self.autoRankLookbackDays or 10
    local arSettings = self.savedVars.autoRanks
    local gConfig = self:GetGuildRankConfig(guildId)

    -- Permission Pre-flight: verify Note Read authority
    local canReadNotes = DoesPlayerHaveGuildPermission and DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_NOTE_READ)
    if canReadNotes == false and arSettings.protectDNR then
        self.PrintChat(string.format("|cFF5555[Security Guard]|r You lack Note Read permission in %s! Automatically restricting to promotions-only to protect DNR members.", guildName))
        arSettings.restrictDemotions = true
    end

    -- Query sales & bank deposits
    local salesByMember = self.GetMemberSales and self:GetMemberSales(guildId, lookbackDays) or {}
    local bankDeposits = {}
    local cutoffTs = GetTimeStamp() - (lookbackDays * 86400)

    if self.savedVars and self.savedVars.staff and self.savedVars.staff.bankDeposits then
        for _, dep in pairs(self.savedVars.staff.bankDeposits) do
            if dep.guildId == guildId and (dep.timestamp or 0) >= cutoffTs and dep.depositor then
                local rawName = string.gsub(dep.depositor, "^@", ""):lower()
                bankDeposits[rawName] = (bankDeposits[rawName] or 0) + (dep.amount or 0)
            end
        end
    end

    -- Self & player authority identification
    local myAccount = string.lower(string.gsub(GetDisplayName() or "", "^@", ""))
    local myMemberIdx = GetGuildMemberIndexFromDisplayName and GetGuildMemberIndexFromDisplayName(guildId, GetDisplayName())
    local myRankIndex = 99
    if myMemberIdx and myMemberIdx > 0 then
        local _, _, mRank = GetGuildMemberInfo(guildId, myMemberIdx)
        if mRank then myRankIndex = mRank end
    end
    local isGM = IsPlayerGuildMaster and IsPlayerGuildMaster(guildId)

    -- Identify base (lowest) active non-ignored member rank
    local baseRankIndex = numRanks
    for r = numRanks, 1, -1 do
        local rCfg = gConfig.ranks[r]
        if rCfg and not rCfg.isOfficer and not rCfg.isIgnored and not self:IsOfficerCapableRank(guildId, r) then
            baseRankIndex = r
            break
        end
    end

    local results = {}
    local nowTs = GetTimeStamp()

    for i = 1, numMembers do
        local displayName, note, rankIndex, playerStatus, secsSinceLogoff = GetGuildMemberInfo(guildId, i)
        local rawName = string.gsub(displayName, "^@", ""):lower()
        local currentRankName = GetFinalGuildRankName(guildId, rankIndex)
        local cleanNote = note or ""
        local noteLower = string.lower(cleanNote)
        local plainNote = string.gsub(string.gsub(noteLower, "|c%x%x%x%x%x%x", ""), "|r", "")

        local sData = salesByMember[rawName] or { count = 0, gold = 0 }
        local salesGold = sData.gold or 0
        local salesCount = sData.count or 0
        local depGold = bankDeposits[rawName] or 0

        -- Shields & Immunity Checks
        local isSelf = (rawName == myAccount)
        local isOfficerRank = self:IsOfficerCapableRank(guildId, rankIndex)
        local isAtOrAboveMyRank = not isGM and (rankIndex <= myRankIndex)
        local currentRCfg = gConfig.ranks[rankIndex]
        local isIgnoredRank = currentRCfg and currentRCfg.isIgnored
        local isDNR = string.find(plainNote, "%f[%w]dnr%f[%W]") ~= nil
        local isLOA = string.find(plainNote, "%f[%w]loa%f[%W]") ~= nil
        local isPerm = string.find(plainNote, "%f[%w]perm%f[%W]") ~= nil
            or string.find(plainNote, "founder") ~= nil
            or string.find(plainNote, "vip") ~= nil
        local isProbation = (arSettings.probationDays > 0)
            and (secsSinceLogoff < (arSettings.probationDays * 86400))
            and (rankIndex == numRanks)

        local action = "KEEP"
        local targetRankIndex = rankIndex
        local statusReason = "Maintained"
        local isExempt = false

        if isSelf then
            statusReason = "🛡 Self Protection"
            action = "KEEP"
            isExempt = true
        elseif isIgnoredRank then
            statusReason = "⭐ Ignored / VIP Rank"
            action = "KEEP"
            isExempt = true
        elseif isOfficerRank and arSettings.officerImmunity then
            statusReason = "🛡 Officer Immunity"
            action = "KEEP"
            isExempt = true
        elseif isAtOrAboveMyRank then
            statusReason = "🛡 At/Above My Rank"
            action = "KEEP"
            isExempt = true
        elseif isDNR and arSettings.protectDNR then
            statusReason = "🛡 DNR Shield"
            action = "KEEP"
            isExempt = true
        elseif isLOA then
            statusReason = "🛡 LOA (Leave of Absence)"
            action = "KEEP"
            isExempt = true
        elseif isPerm then
            statusReason = "🛡 Permanent Member"
            action = "KEEP"
            isExempt = true
        elseif isProbation then
            statusReason = "🛡 Probation Grace"
            action = "KEEP"
            isExempt = true
        else
            -- Top-to-Bottom Ladder Walk
            local qualifiedRankIdx = baseRankIndex
            local qualifiedReason = "Base Member Rank"

            -- Evaluate ranks from highest member rank tier down to lowest
            for r = 1, numRanks do
                local rCfg = gConfig.ranks[r]
                if rCfg and not rCfg.isOfficer and not rCfg.isIgnored and not self:IsOfficerCapableRank(guildId, r) then
                    local minS = rCfg.minSales or 0
                    local minC = rCfg.minSalesCount or 0
                    local minD = rCfg.minDues or 0
                    local mode = rCfg.mode or "OR"
                    local met = false

                    local hasMinSales = (minS > 0)
                    local hasMinCount = (minC > 0)
                    local hasMinDues = (minD > 0)

                    if not hasMinSales and not hasMinCount and not hasMinDues then
                        met = true
                    elseif mode == "OR" then
                        local metSales = hasMinSales and (salesGold >= minS)
                        local metCount = hasMinCount and (salesCount >= minC)
                        local metDues = hasMinDues and (depGold >= minD)
                        met = metSales or metCount or metDues
                    elseif mode == "AND" then
                        local metSales = (not hasMinSales) or (salesGold >= minS)
                        local metCount = (not hasMinCount) or (salesCount >= minC)
                        local metDues = (not hasMinDues) or (depGold >= minD)
                        met = metSales and metCount and metDues
                    elseif mode == "SUM" then
                        local metSum = (salesGold + depGold) >= math.max(minS, minD)
                        local metCount = (not hasMinCount) or (salesCount >= minC)
                        met = metSum and metCount
                    end

                    if met then
                        qualifiedRankIdx = r
                        if hasMinSales or hasMinCount or hasMinDues then
                            local parts = {}
                            if hasMinCount and salesCount >= minC then table.insert(parts, string.format("%d sales", salesCount)) end
                            if hasMinSales and salesGold >= minS then table.insert(parts, string.format("%sg sales", ZO_LocalizeDecimalNumber(salesGold))) end
                            if hasMinDues and depGold >= minD then table.insert(parts, string.format("%sg dues", ZO_LocalizeDecimalNumber(depGold))) end
                            local detail = (#parts > 0) and table.concat(parts, ", ") or string.format("Sales: %sg, Dues: %sg", ZO_LocalizeDecimalNumber(salesGold), ZO_LocalizeDecimalNumber(depGold))
                            qualifiedReason = string.format("Qualified %s (%s)", rCfg.name, detail)
                        else
                            qualifiedReason = string.format("Base Rank (%s)", rCfg.name)
                        end
                        break
                    end
                end
            end

            targetRankIndex = qualifiedRankIdx

            -- In ESO: Lower rankIndex number = Higher guild authority
            if rankIndex > targetRankIndex then
                action = "PROMOTE"
                statusReason = qualifiedReason
            elseif rankIndex < targetRankIndex then
                action = "DEMOTE"
                statusReason = string.format("Below %s criteria", currentRankName)
            else
                action = "KEEP"
                statusReason = "Rank Maintained"
            end

            -- Apply Restrict Demotions safety option
            if arSettings.restrictDemotions and action == "DEMOTE" then
                action = "KEEP"
                targetRankIndex = rankIndex
                statusReason = "Demotion Restricted"
            end

            -- Apply Demote Step Cap (e.g. max 1 tier drop per evaluation)
            if action == "DEMOTE" and arSettings.demoteCap and arSettings.demoteCap > 0 then
                local maxDrop = rankIndex + arSettings.demoteCap
                if targetRankIndex > maxDrop then
                    targetRankIndex = maxDrop
                end
                if targetRankIndex == rankIndex then
                    action = "KEEP"
                    statusReason = "Demotion Capped"
                end
            end
        end

        local targetRankName = GetFinalGuildRankName(guildId, targetRankIndex) or ("Rank " .. targetRankIndex)

        table.insert(results, {
            memberIndex = i,
            displayName = displayName,
            note = cleanNote,
            currentRankIndex = rankIndex,
            currentRankName = currentRankName,
            targetRankIndex = targetRankIndex,
            targetRankName = targetRankName,
            action = action,
            isExempt = isExempt,
            salesGold = salesGold,
            salesCount = salesCount,
            donations = depGold,
            status = statusReason,
            selected = (action == "PROMOTE" or action == "DEMOTE"),
        })
    end

    -- Sort: Promotions first, then Demotions, then Keep / Exempt
    table.sort(results, function(a, b)
        local order = { PROMOTE = 1, DEMOTE = 2, KEEP = 3 }
        local oa = order[a.action] or 4
        local ob = order[b.action] or 4
        if oa ~= ob then return oa < ob end
        if a.action == "DEMOTE" and b.action == "DEMOTE" then
            return a.currentRankIndex < b.currentRankIndex
        end
        return a.displayName:lower() < b.displayName:lower()
    end)

    self.autoRankResults = results
    self.autoRankCurrentPage = 1
    return results
end

--[[ =========================================================================
     ACK-GATED BATCH EXECUTION ENGINE
========================================================================= ]]--

local MIN_SPACING_MS = 400
local ACK_TIMEOUT_MS = 4000

function FR:CleanupAutoRankBatch()
    EVENT_MANAGER:UnregisterForUpdate("FissalRelay_AutoRankBatch")
    EVENT_MANAGER:UnregisterForEvent("FissalRelay_AutoRankAck", EVENT_GUILD_MEMBER_RANK_CHANGED)
    EVENT_MANAGER:UnregisterForEvent("FissalRelay_AutoRankSchemaDrift1", EVENT_GUILD_RANKS_CHANGED)
    EVENT_MANAGER:UnregisterForEvent("FissalRelay_AutoRankSchemaDrift2", EVENT_GUILD_RANK_CHANGED)
    self.autoRankBatchRunning = false
    self.autoRankPending = nil

    if self.autoRanksApplyBtn then self.autoRanksApplyBtn:SetHidden(false) end
    if self.autoRanksAbortBtn then self.autoRanksAbortBtn:SetHidden(true) end
end

function FR:StartAutoRankBatch(guildId)
    if self.autoRankBatchRunning then
        self.PrintChat("|cFF5555[Fissal Ranks]|r Batch is already running!")
        return
    end
    self:CleanupAutoRankBatch()

    guildId = self:ResolveGuildId(guildId or self.selectedGuildIndex or 1)
    local guildName = GetGuildName(guildId)

    local hasPromote = DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_PROMOTE)
    local hasDemote = DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_DEMOTE)
    local isGM = IsPlayerGuildMaster and IsPlayerGuildMaster(guildId)

    if not (isGM or (hasPromote and hasDemote)) then
        self.PrintChat(string.format("|cFF5555Permission Denied:|r You need both Promote and Demote permissions in %s to apply rank changes.", guildName))
        return
    end

    local setRankFn = GuildSetRank or GuildSetMemberRank or SetGuildMemberRank
    if not setRankFn then
        self.PrintChat("|cFF5555[Error]|r Set guild rank API function not found on client! Aborting batch.")
        return
    end

    local tasks = {}
    for _, item in ipairs(self.autoRankResults or {}) do
        if item.selected and item.action ~= "KEEP" and item.targetRankIndex ~= item.currentRankIndex then
            table.insert(tasks, item)
        end
    end

    if #tasks == 0 then
        self.PrintChat("|cFF9900[Fissal Ranks]|r No rank changes selected to apply.")
        return
    end

    self.autoRankTasks = tasks
    self.autoRankBatchRunning = true
    self.autoRankTotalTasks = #tasks
    self.autoRankProcessedCount = 0
    self.autoRankSucceeded = 0
    self.autoRankTimedOut = 0
    self.autoRankSkipped = 0
    self.autoRankPending = nil
    self.autoRankSentAt = 0
    self.autoRankNextAllowedAt = 0

    if self.autoRanksApplyBtn then self.autoRanksApplyBtn:SetHidden(true) end
    if self.autoRanksAbortBtn then self.autoRanksAbortBtn:SetHidden(false) end

    self.PrintChat(string.format("⚡ |c00FFCC[Fissal Ranks]|r Starting paced batch for %s (%d changes)...", guildName, #tasks))

    -- Register Server Ack Listener
    EVENT_MANAGER:RegisterForEvent("FissalRelay_AutoRankAck", EVENT_GUILD_MEMBER_RANK_CHANGED, function(_, eventGuildId, displayName, newRankIndex)
        if not FR.autoRankBatchRunning or eventGuildId ~= guildId then return end
        if FR.autoRankPending and FR.autoRankPending.displayName == displayName then
            FR.autoRankSucceeded = (FR.autoRankSucceeded or 0) + 1
            FR.autoRankProcessedCount = (FR.autoRankProcessedCount or 0) + 1
            FR.autoRankPending = nil
            FR.autoRankNextAllowedAt = GetGameTimeMilliseconds() + MIN_SPACING_MS

            if FR.autoRanksProgressLbl then
                FR.autoRanksProgressLbl:SetText(string.format("Applying: %d / %d", FR.autoRankProcessedCount, FR.autoRankTotalTasks))
            end
        end
    end)

    local taskIdx = 1

    EVENT_MANAGER:RegisterForUpdate("FissalRelay_AutoRankBatch", 50, function()
        if not FR.autoRankBatchRunning then return end
        local now = GetGameTimeMilliseconds()

        if FR.autoRankPending then
            if (now - FR.autoRankSentAt) > ACK_TIMEOUT_MS then
                local p = FR.autoRankPending
                local mIdx = GetGuildMemberIndexFromDisplayName and GetGuildMemberIndexFromDisplayName(guildId, p.displayName)
                local _, _, liveRank = (mIdx and mIdx > 0) and GetGuildMemberInfo(guildId, mIdx)
                if liveRank == p.targetRankIndex then
                    FR.autoRankSucceeded = (FR.autoRankSucceeded or 0) + 1
                else
                    FR.autoRankTimedOut = (FR.autoRankTimedOut or 0) + 1
                end
                FR.autoRankPending = nil
                FR.autoRankProcessedCount = (FR.autoRankProcessedCount or 0) + 1
                FR.autoRankNextAllowedAt = now + MIN_SPACING_MS

                if FR.autoRanksProgressLbl then
                    FR.autoRanksProgressLbl:SetText(string.format("Applying: %d / %d", FR.autoRankProcessedCount, FR.autoRankTotalTasks))
                end
            else
                return
            end
        end

        if now < FR.autoRankNextAllowedAt then return end

        if taskIdx > #FR.autoRankTasks then
            FR:CleanupAutoRankBatch()
            FR.PrintChat(string.format("✓ |c59E08A[Fissal Ranks]|r Batch Complete: %d Succeeded, %d Timed Out, %d Skipped",
                FR.autoRankSucceeded or 0, FR.autoRankTimedOut or 0, FR.autoRankSkipped or 0))
            PlaySound(SOUNDS.LEVEL_UP or SOUNDS.GUILD_ROSTER_ADDED)
            FR:EvaluateAutoRanks(guildId)
            FR:UpdateAutoRanksUI()
            return
        end

        local item = FR.autoRankTasks[taskIdx]
        taskIdx = taskIdx + 1

        if item then
            local memberIdx = GetGuildMemberIndexFromDisplayName and GetGuildMemberIndexFromDisplayName(guildId, item.displayName)
            if not memberIdx or memberIdx <= 0 then
                FR.autoRankSkipped = (FR.autoRankSkipped or 0) + 1
                FR.autoRankProcessedCount = (FR.autoRankProcessedCount or 0) + 1
                FR.autoRankNextAllowedAt = now + 100
                return
            end

            local _, _, currentRank = GetGuildMemberInfo(guildId, memberIdx)
            if currentRank == item.targetRankIndex then
                FR.autoRankSkipped = (FR.autoRankSkipped or 0) + 1
                FR.autoRankProcessedCount = (FR.autoRankProcessedCount or 0) + 1
                FR.autoRankNextAllowedAt = now + 100
                return
            end

            FR.autoRankPending = item
            FR.autoRankSentAt = now
            setRankFn(guildId, item.displayName, item.targetRankIndex)
        end
    end)
end

function FR:AbortAutoRankBatch()
    if not self.autoRankBatchRunning then return end
    self:CleanupAutoRankBatch()
    if self.autoRanksProgressLbl then self.autoRanksProgressLbl:SetText("Aborted") end
    self.PrintChat("|cFF5555[Fissal Ranks]|r Batch execution aborted by staff.")
    PlaySound(SOUNDS.GENERAL_ALERT_ERROR or SOUNDS.NOTE_DISCARDED)
end

--[[ =========================================================================
     UI CONSTRUCTION (CONSOLE TAB 6)
========================================================================= ]]--

function FR:BuildAutoRanksUI(parent)
    local wm = WINDOW_MANAGER
    local panel = wm:CreateControl("FissalRelay_Console_Tab6", parent, CT_CONTROL)
    panel:SetAnchorFill()
    panel:SetHidden(true)
    self.consoleTabs[6] = panel

    -- 1. Main Container Card
    local card = wm:CreateControl("$(parent)_Card", panel, CT_BACKDROP)
    card:SetAnchorFill()
    card:SetCenterColor(0.06, 0.06, 0.08, 0.85)
    card:SetEdgeColor(0.30, 0.25, 0.18, 0.70)
    card:SetEdgeTexture("", 8, 1, 0)
    card:SetMouseEnabled(true)
    card:SetHandler("OnMouseWheel", function(control, delta)
        local total = #self.autoRankFilteredResults
        local maxPages = math.max(1, math.ceil(total / ROWS_PER_PAGE))
        if delta < 0 and self.autoRankCurrentPage < maxPages then
            self.autoRankCurrentPage = self.autoRankCurrentPage + 1
            self:RenderAutoRanksRows()
        elseif delta > 0 and self.autoRankCurrentPage > 1 then
            self.autoRankCurrentPage = self.autoRankCurrentPage - 1
            self:RenderAutoRanksRows()
        end
    end)

    -- 2. Title & Status
    local title = wm:CreateControl("$(parent)_Title", card, CT_LABEL)
    title:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 8)
    title:SetDimensions(360, 22)
    title:SetFont("ZoFontGameBold")
    title:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
    title:SetText("|cFF9900AUTO-RANK LEADERBOARD|r • |c00FFCCDues & Performance|r")
    self.autoRanksTitleLbl = title

    local statSummaryLbl = wm:CreateControl("$(parent)_Stats", card, CT_LABEL)
    statSummaryLbl:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 8)
    statSummaryLbl:SetDimensions(480, 22)
    statSummaryLbl:SetHorizontalAlignment(TEXT_ALIGN_RIGHT)
    statSummaryLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
    statSummaryLbl:SetFont("ZoFontGameSmall")
    statSummaryLbl:SetText("Evaluated: --  |  Promote: --  |  Demote: --  |  Kept: --")
    self.autoRanksSummaryLbl = statSummaryLbl

    -- 3. Filter Bar & Action Buttons
    local controlRow = wm:CreateControl("$(parent)_Controls", card, CT_CONTROL)
    controlRow:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 32)
    controlRow:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 32)
    controlRow:SetHeight(28)
    self.autoRanksControlRow = controlRow

    -- Filter Buttons: All (38), Changes (56), Promote (68), Demote (68), Exempt (58)
    local filters = {
        { id = "all", label = "All", width = 38 },
        { id = "changes", label = "Changes", width = 56 },
        { id = "promote", label = "|t14:14:EsoUI/Art/Buttons/pointsplus_up.dds|t Promote", width = 68 },
        { id = "demote", label = "|t14:14:EsoUI/Art/Buttons/pointsplus_down.dds|t Demote", width = 68 },
        { id = "exempt", label = "|t14:14:EsoUI/Art/Campaign/overview_guildOwner_icon.dds|t Exempt", width = 58 },
    }
    self.autoRankFilterBtns = {}

    local curX = 0
    for _, f in ipairs(filters) do
        local btn = wm:CreateControl("$(parent)_F_" .. f.id, controlRow, CT_BUTTON)
        btn:SetAnchor(TOPLEFT, controlRow, TOPLEFT, curX, 2)
        btn:SetDimensions(f.width, 24)
        btn:SetFont("ZoFontGameSmall")
        btn:SetText(f.label)
        self:StyleTactileButton(btn, {
            normalBg = { 0.08, 0.08, 0.12, 0.85 },
            hoverBg = { 0.12, 0.18, 0.22, 0.95 },
            normalEdge = { 0.30, 0.30, 0.35, 0.65 },
            hoverEdge = { 0, 0.85, 0.75, 1.0 },
        })
        btn:SetHandler("OnClicked", function()
            self.autoRankFilter = f.id
            self.autoRankCurrentPage = 1
            self:UpdateAutoRanksFilterButtons()
            self:RefreshAutoRanksGrid()
        end)
        self.autoRankFilterBtns[f.id] = btn
        curX = curX + f.width + 3
    end

    -- Lookback Window cycle button (width 54)
    local windowBtn = wm:CreateControl("$(parent)_WindowBtn", controlRow, CT_BUTTON)
    windowBtn:SetAnchor(TOPLEFT, controlRow, TOPLEFT, curX + 3, 2)
    windowBtn:SetDimensions(54, 24)
    windowBtn:SetFont("ZoFontGameSmall")
    windowBtn:SetText("10 Days")
    self:StyleTactileButton(windowBtn, {
        normalBg = { 0.10, 0.08, 0.04, 0.85 },
        hoverBg = { 0.18, 0.14, 0.06, 0.95 },
        normalEdge = { 0.60, 0.45, 0.15, 0.70 },
        hoverEdge = { 0.95, 0.70, 0.20, 1.0 },
        tooltipTitle = "Lookback Window",
        tooltipText = "Cycle the sales & bank dues audit window (7, 10, 14, 15, or 30 days).",
    })
    windowBtn:SetHandler("OnClicked", function()
        local cycles = { 7, 10, 14, 15, 30 }
        local cur = self.autoRankLookbackDays or 10
        local nextVal = 10
        for idx, v in ipairs(cycles) do
            if v == cur then
                nextVal = cycles[(idx % #cycles) + 1]
                break
            end
        end
        self.autoRankLookbackDays = nextVal
        if self.savedVars and self.savedVars.autoRanks then
            self.savedVars.autoRanks.lookbackDays = nextVal
        end
        windowBtn:SetText(nextVal .. " Days")
        local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
        self:EvaluateAutoRanks(gId)
        self:UpdateAutoRanksUI()
    end)
    self.autoRanksWindowBtn = windowBtn

    -- Right Action Buttons: Explicit non-colliding coordinates from TOPRIGHT
    local applyBtn = wm:CreateControl("$(parent)_ApplyBtn", controlRow, CT_BUTTON)
    applyBtn:SetAnchor(TOPRIGHT, controlRow, TOPRIGHT, 0, 2)
    applyBtn:SetDimensions(125, 24)
    applyBtn:SetFont("ZoFontGameBold")
    applyBtn:SetText("Apply Changes")
    self:StyleTactileButton(applyBtn, {
        normalBg = { 0.20, 0.12, 0.04, 0.95 },
        hoverBg = { 0.28, 0.18, 0.06, 1.00 },
        normalEdge = { 0.95, 0.65, 0.15, 0.95 },
        hoverEdge = { 1.00, 0.85, 0.25, 1.00 },
        normalTextColor = { 1, 0.85, 0.20, 1 },
    })
    applyBtn:SetHandler("OnClicked", function()
        local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
        self:StartAutoRankBatch(gId)
    end)
    self.autoRanksApplyBtn = applyBtn

    local abortBtn = wm:CreateControl("$(parent)_AbortBtn", controlRow, CT_BUTTON)
    abortBtn:SetAnchor(TOPRIGHT, controlRow, TOPRIGHT, 0, 2)
    abortBtn:SetDimensions(125, 24)
    abortBtn:SetFont("ZoFontGameBold")
    abortBtn:SetText("|cFF5555[STOP / ABORT]|r")
    abortBtn:SetHidden(true)
    self:StyleTactileButton(abortBtn, {
        normalBg = { 0.25, 0.05, 0.05, 0.95 },
        hoverBg = { 0.35, 0.08, 0.08, 1.00 },
        normalEdge = { 0.95, 0.25, 0.25, 1.00 },
        hoverEdge = { 1.00, 0.40, 0.40, 1.00 },
        normalTextColor = { 1, 0.4, 0.4, 1 },
    })
    abortBtn:SetHandler("OnClicked", function()
        self:AbortAutoRankBatch()
    end)
    self.autoRanksAbortBtn = abortBtn

    local evalBtn = wm:CreateControl("$(parent)_EvalBtn", controlRow, CT_BUTTON)
    evalBtn:SetAnchor(TOPRIGHT, controlRow, TOPRIGHT, -131, 2)
    evalBtn:SetDimensions(72, 24)
    evalBtn:SetFont("ZoFontGameBold")
    evalBtn:SetText("Evaluate")
    self:StyleTactileButton(evalBtn, {
        normalBg = { 0.04, 0.14, 0.14, 0.90 },
        hoverBg = { 0.06, 0.22, 0.20, 0.98 },
        normalEdge = { 0, 0.80, 0.70, 0.85 },
        hoverEdge = { 0, 1.00, 0.90, 1.00 },
        normalTextColor = { 0, 1, 0.85, 1 },
    })
    evalBtn:SetHandler("OnClicked", function()
        local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
        self:EvaluateAutoRanks(gId)
        self:UpdateAutoRanksUI()
    end)

    -- [✉ Auto-Welcome] Button (width 110, anchored at -209)
    local welcomeBtn = wm:CreateControl("$(parent)_WelcomeBtn", controlRow, CT_BUTTON)
    welcomeBtn:SetAnchor(TOPRIGHT, controlRow, TOPRIGHT, -209, 2)
    welcomeBtn:SetDimensions(110, 24)
    welcomeBtn:SetFont("ZoFontGameBold")
    welcomeBtn:SetText("✉ Auto-Welcome")
    self:StyleTactileButton(welcomeBtn, {
        normalBg = { 0.08, 0.08, 0.12, 0.85 },
        hoverBg = { 0.12, 0.18, 0.22, 0.95 },
        normalEdge = { 0.30, 0.30, 0.35, 0.65 },
        hoverEdge = { 0, 0.85, 0.75, 1.0 },
        normalTextColor = { 0.7, 0.8, 0.85, 1 },
        tooltipTitle = "Auto-Welcome Recruits",
        tooltipText = "View pending guild recruits, configure onboarding welcome letter, and dispatch welcome mails.",
    })
    welcomeBtn:SetHandler("OnClicked", function()
        self:ToggleAutoWelcomeDrawer()
    end)
    self.autoRanksWelcomeBtn = welcomeBtn

    -- [⚙ Rank Dues] Button (width 96, anchored at -331)
    local configBtn = wm:CreateControl("$(parent)_ConfigBtn", controlRow, CT_BUTTON)
    configBtn:SetAnchor(TOPRIGHT, controlRow, TOPRIGHT, -331, 2)
    configBtn:SetDimensions(96, 24)
    configBtn:SetFont("ZoFontGameBold")
    configBtn:SetText("⚙ Rank Dues")
    self:StyleTactileButton(configBtn, {
        normalBg = { 0.06, 0.12, 0.18, 0.90 },
        hoverBg = { 0.10, 0.20, 0.28, 0.98 },
        normalEdge = { 0.20, 0.60, 0.90, 0.85 },
        hoverEdge = { 0.40, 0.80, 1.00, 1.00 },
        normalTextColor = { 0.4, 0.85, 1, 1 },
        hoverTextColor = { 0.7, 0.95, 1, 1 },
        tooltipTitle = "Configure Rank Dues",
        tooltipText = "Open the rank threshold setup drawer to customize sales and bank dues requirements for each guild rank.",
    })
    configBtn:SetHandler("OnClicked", function()
        self:ToggleRankConfigDrawer()
    end)
    self.autoRanksConfigBtn = configBtn

    local progLbl = wm:CreateControl("$(parent)_ProgLbl", controlRow, CT_LABEL)
    progLbl:SetAnchor(TOPRIGHT, controlRow, TOPRIGHT, -435, 6)
    progLbl:SetFont("ZoFontGameSmall")
    progLbl:SetText("")
    self.autoRanksProgressLbl = progLbl

    -- 4. Table Header Row
    local headerRow = wm:CreateControl("$(parent)_Header", card, CT_BACKDROP)
    headerRow:SetAnchor(TOPLEFT, card, TOPLEFT, 10, 64)
    headerRow:SetAnchor(TOPRIGHT, card, TOPRIGHT, -10, 64)
    headerRow:SetHeight(24)
    headerRow:SetCenterColor(0.08, 0.08, 0.12, 0.95)
    headerRow:SetEdgeColor(0.25, 0.25, 0.30, 0.65)
    headerRow:SetEdgeTexture("", 8, 1, 0)
    self.autoRanksHeaderRow = headerRow

    -- Master Checkbox
    local masterCheck = wm:CreateControl("$(parent)_MasterCheck", headerRow, CT_BUTTON)
    masterCheck:SetAnchor(LEFT, headerRow, LEFT, 8, 0)
    masterCheck:SetDimensions(20, 20)
    masterCheck:SetFont("ZoFontGameBold")
    masterCheck:SetText("[X]")
    masterCheck:SetNormalFontColor(0, 1, 0.8, 1)
    masterCheck.allSelected = true
    masterCheck:SetHandler("OnClicked", function()
        masterCheck.allSelected = not masterCheck.allSelected
        masterCheck:SetText(masterCheck.allSelected and "[X]" or "[ ]")
        for _, item in ipairs(self.autoRankFilteredResults or {}) do
            if item.action ~= "KEEP" then
                item.selected = masterCheck.allSelected
            end
        end
        self:RenderAutoRanksRows()
    end)

    local function MakeHdrLbl(name, anchorCtrl, anchorPoint, toPoint, x, width, text)
        local l = wm:CreateControl("$(parent)_" .. name, headerRow, CT_LABEL)
        l:SetAnchor(anchorPoint, anchorCtrl, toPoint, x, 0)
        l:SetDimensions(width, 22)
        l:SetFont("ZoFontGameBold")
        l:SetColor(0.80, 0.78, 0.70, 1)
        l:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        l:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        l:SetText(text)
        return l
    end

    local hMember = MakeHdrLbl("HMember", masterCheck, LEFT, RIGHT, 8, 175, "MEMBER (@NAME)")
    local hCurRank = MakeHdrLbl("HCurRank", hMember, LEFT, RIGHT, 6, 115, "CURRENT RANK")
    local hTgtRank = MakeHdrLbl("HTgtRank", hCurRank, LEFT, RIGHT, 6, 125, "TARGET RANK")
    local hAction = MakeHdrLbl("HAction", hTgtRank, LEFT, RIGHT, 6, 95, "ACTION")
    local hSales = MakeHdrLbl("HSales", hAction, LEFT, RIGHT, 6, 90, "SALES GOLD")
    local hDep = MakeHdrLbl("HDep", hSales, LEFT, RIGHT, 6, 85, "BANK DUES")
    local hNote = MakeHdrLbl("HNote", hDep, LEFT, RIGHT, 6, 130, "ASSESSMENT & NOTES")

    -- 5. Data Rows (13 Spacious Rows with Mouse Wheel Navigation)
    self.autoRanksGridRows = {}
    local rowStartY = 92
    local rowHeight = 25

    for r = 1, ROWS_PER_PAGE do
        local row = wm:CreateControl("$(parent)_Row" .. r, card, CT_BACKDROP)
        row:SetAnchor(TOPLEFT, card, TOPLEFT, 10, rowStartY + (r - 1) * (rowHeight + 2))
        row:SetAnchor(TOPRIGHT, card, TOPRIGHT, -10, rowStartY + (r - 1) * (rowHeight + 2))
        row:SetHeight(rowHeight)
        row:SetCenterColor(0.04, 0.04, 0.06, 0.60)
        row:SetEdgeColor(0.18, 0.16, 0.12, 0.40)
        row:SetEdgeTexture("", 8, 1, 0)
        row:SetMouseEnabled(true)

        local checkBtn = wm:CreateControl("$(parent)_Check", row, CT_BUTTON)
        checkBtn:SetAnchor(LEFT, row, LEFT, 8, 0)
        checkBtn:SetDimensions(20, 20)
        checkBtn:SetFont("ZoFontGameSmall")
        checkBtn:SetText("[X]")

        local memberLbl = wm:CreateControl("$(parent)_Member", row, CT_LABEL)
        memberLbl:SetAnchor(LEFT, checkBtn, RIGHT, 8, 0)
        memberLbl:SetDimensions(175, 20)
        memberLbl:SetFont("ZoFontGameSmall")
        memberLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        memberLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)

        local curRankLbl = wm:CreateControl("$(parent)_CurRank", row, CT_LABEL)
        curRankLbl:SetAnchor(LEFT, memberLbl, RIGHT, 6, 0)
        curRankLbl:SetDimensions(115, 20)
        curRankLbl:SetFont("ZoFontGameSmall")
        curRankLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        curRankLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)

        local tgtRankLbl = wm:CreateControl("$(parent)_TgtRank", row, CT_LABEL)
        tgtRankLbl:SetAnchor(LEFT, curRankLbl, RIGHT, 6, 0)
        tgtRankLbl:SetDimensions(125, 20)
        tgtRankLbl:SetFont("ZoFontGameSmall")
        tgtRankLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        tgtRankLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)

        local actLbl = wm:CreateControl("$(parent)_Act", row, CT_LABEL)
        actLbl:SetAnchor(LEFT, tgtRankLbl, RIGHT, 6, 0)
        actLbl:SetDimensions(95, 20)
        actLbl:SetFont("ZoFontGameSmall")
        actLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)

        local salesLbl = wm:CreateControl("$(parent)_Sales", row, CT_LABEL)
        salesLbl:SetAnchor(LEFT, actLbl, RIGHT, 6, 0)
        salesLbl:SetDimensions(90, 20)
        salesLbl:SetFont("ZoFontGameSmall")
        salesLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)

        local depLbl = wm:CreateControl("$(parent)_Dep", row, CT_LABEL)
        depLbl:SetAnchor(LEFT, salesLbl, RIGHT, 6, 0)
        depLbl:SetDimensions(85, 20)
        depLbl:SetFont("ZoFontGameSmall")
        depLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)

        local noteLbl = wm:CreateControl("$(parent)_Note", row, CT_LABEL)
        noteLbl:SetAnchor(LEFT, depLbl, RIGHT, 6, 0)
        noteLbl:SetAnchor(RIGHT, row, RIGHT, -8, 0)
        noteLbl:SetHeight(20)
        noteLbl:SetMaxLineCount(1)
        noteLbl:SetFont("ZoFontGameSmall")
        noteLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        noteLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)

        row:SetHandler("OnMouseEnter", function(control)
            control:SetCenterColor(0.10, 0.10, 0.16, 0.90)
            control:SetEdgeColor(0.90, 0.70, 0.20, 0.85)
            if control.memberData then
                local m = control.memberData
                InitializeTooltip(InformationTooltip, control, RIGHT, 5, 0)
                InformationTooltip:AddLine(string.format("|c00FFCC%s|r", m.displayName), "ZoFontGameBold")
                InformationTooltip:AddLine(string.format("• Current Rank: |cFFFFFF%s|r (#%d)", m.currentRankName, m.currentRankIndex), "ZoFontGameSmall")
                InformationTooltip:AddLine(string.format("• Target Rank: |c59E08A%s|r (#%d)", m.targetRankName, m.targetRankIndex), "ZoFontGameSmall")
                InformationTooltip:AddLine(string.format("• Action: |c%s%s|r",
                    m.action == "PROMOTE" and "59E08A" or (m.action == "DEMOTE" and "FF6666" or "888888"), m.action), "ZoFontGameSmall")
                InformationTooltip:AddLine(string.format("• Sales: |cFFD700%s gold|r (%d sales in lookback)", ZO_LocalizeDecimalNumber(m.salesGold), m.salesCount), "ZoFontGameSmall")
                InformationTooltip:AddLine(string.format("• Bank Dues / Deposits: |c59E08A%s gold|r", ZO_LocalizeDecimalNumber(m.donations)), "ZoFontGameSmall")
                InformationTooltip:AddLine(string.format("• Assessment: |cE6C387%s|r", m.status or "--"), "ZoFontGameSmall")
                if m.note and m.note ~= "" then
                    InformationTooltip:AddLine(string.format("• Guild Note: |c888888%s|r", m.note), "ZoFontGameSmall")
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

        self.autoRanksGridRows[r] = {
            row = row,
            check = checkBtn,
            member = memberLbl,
            curRank = curRankLbl,
            tgtRank = tgtRankLbl,
            action = actLbl,
            sales = salesLbl,
            dep = depLbl,
            note = noteLbl,
        }
    end

    -- 6. Footer & Pagination Controls
    local footerY = -8
    local footerStatLbl = wm:CreateControl("$(parent)_FooterStat", card, CT_LABEL)
    footerStatLbl:SetAnchor(BOTTOMLEFT, card, BOTTOMLEFT, 12, footerY)
    footerStatLbl:SetFont("ZoFontGameSmall")
    footerStatLbl:SetText("Auto-Ranks: Ready  •  Scroll wheel to page")
    self.autoRanksFooterLbl = footerStatLbl

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
    })
    nextBtn:SetHandler("OnClicked", function()
        local total = #self.autoRankFilteredResults
        local maxPages = math.max(1, math.ceil(total / ROWS_PER_PAGE))
        if self.autoRankCurrentPage < maxPages then
            self.autoRankCurrentPage = self.autoRankCurrentPage + 1
            self:RenderAutoRanksRows()
        end
    end)

    local pageLbl = wm:CreateControl("$(parent)_PageLbl", card, CT_LABEL)
    pageLbl:SetAnchor(RIGHT, nextBtn, LEFT, -8, 0)
    pageLbl:SetFont("ZoFontGameSmall")
    pageLbl:SetText("Page 1/1")
    self.autoRanksPageLbl = pageLbl

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
    })
    prevBtn:SetHandler("OnClicked", function()
        if self.autoRankCurrentPage > 1 then
            self.autoRankCurrentPage = self.autoRankCurrentPage - 1
            self:RenderAutoRanksRows()
        end
    end)

    -- 7. Rank Dues Configuration Drawer Modal
    self:BuildRankConfigDrawer(card)
    self:BuildAutoWelcomeDrawer(card)
end

--[[ =========================================================================
     RANK DUES CONFIGURATION DRAWER MODAL
========================================================================= ]]--

function FR:BuildRankConfigDrawer(parent)
    local wm = WINDOW_MANAGER
    local drawer = wm:CreateControl("$(parent)_RankDrawer", parent, CT_CONTROL)
    drawer:SetAnchor(TOPLEFT, parent, TOPLEFT, 4, 4)
    drawer:SetAnchor(BOTTOMRIGHT, parent, BOTTOMRIGHT, -4, -4)
    drawer:SetMouseEnabled(true)
    drawer:SetHandler("OnMouseWheel", function() end)
    drawer:SetHidden(true)
    self.rankConfigDrawer = drawer

    -- Dedicated opaque plate guaranteeing zero label or texture bleed-through
    local bg = wm:CreateControl("$(parent)_Bg", drawer, CT_BACKDROP)
    bg:SetAnchorFill()
    bg:SetCenterColor(0.04, 0.04, 0.07, 1.0)
    bg:SetEdgeColor(0.95, 0.70, 0.20, 0.95)
    bg:SetEdgeTexture("", 8, 1, 0)
    bg:SetDrawLayer(DL_BACKGROUND)
    bg:SetDrawLevel(0)

    local defBg = wm:CreateControlFromVirtual("$(parent)_DefBg", drawer, "ZO_DefaultBackdrop")
    defBg:SetAnchorFill()
    defBg:SetAlpha(1.0)
    defBg:SetDrawLayer(DL_BACKGROUND)
    defBg:SetDrawLevel(1)

    local munge = wm:CreateControl("$(parent)_Munge", drawer, CT_TEXTURE)
    munge:SetAnchorFill()
    munge:SetTexture("EsoUI/Art/Performance/StatusMeterMunge.dds")
    munge:SetColor(0.05, 0.04, 0.08, 0.98)
    munge:SetDrawLayer(DL_BACKGROUND)
    munge:SetDrawLevel(2)

    -- Drawer Header
    local dTitle = wm:CreateControl("$(parent)_Title", drawer, CT_LABEL)
    dTitle:SetAnchor(TOPLEFT, drawer, TOPLEFT, 16, 10)
    dTitle:SetFont("ZoFontGameBold")
    dTitle:SetText("|cFF9900RANK DUES & LEADERBOARD THRESHOLDS|r  |c00FFCC(Setup Ladder)|r")

    local closeBtn = wm:CreateControl("$(parent)_CloseBtn", drawer, CT_BUTTON)
    closeBtn:SetAnchor(TOPRIGHT, drawer, TOPRIGHT, -14, 10)
    closeBtn:SetDimensions(26, 26)
    closeBtn:SetFont("ZoFontGameBold")
    closeBtn:SetText("X")
    self:StyleTactileButton(closeBtn, {
        normalBg = { 0.15, 0.05, 0.05, 0.85 },
        hoverBg = { 0.30, 0.08, 0.08, 0.95 },
        normalEdge = { 0.60, 0.20, 0.20, 0.80 },
        hoverEdge = { 1.00, 0.30, 0.30, 1.00 },
        normalTextColor = { 1, 0.5, 0.5, 1 },
        hoverTextColor = { 1, 0.8, 0.8, 1 },
        tooltipTitle = "Close Rank Dues Setup",
    })
    closeBtn:SetHandler("OnClicked", function()
        self:CloseRankConfigDrawer()
    end)

    local dSub = wm:CreateControl("$(parent)_Sub", drawer, CT_LABEL)
    dSub:SetAnchor(TOPLEFT, drawer, TOPLEFT, 16, 32)
    dSub:SetFont("ZoFontGameSmall")
    dSub:SetText("Configure minimum sales gold, sale counts, and bank dues per rank. Ladder evaluates from top rank down.")

    -- Column Headers Bar
    local hdrBar = wm:CreateControl("$(parent)_ColHdr", drawer, CT_BACKDROP)
    hdrBar:SetAnchor(TOPLEFT, drawer, TOPLEFT, 16, 52)
    hdrBar:SetAnchor(TOPRIGHT, drawer, TOPRIGHT, -16, 52)
    hdrBar:SetHeight(20)
    hdrBar:SetCenterColor(0.06, 0.06, 0.09, 0.80)
    hdrBar:SetEdgeColor(0.20, 0.20, 0.25, 0.40)
    hdrBar:SetEdgeTexture("", 8, 1, 0)

    local function MakeColHdr(name, anchorCtrl, anchorPoint, toPoint, x, width, text)
        local l = wm:CreateControl("$(parent)_" .. name, hdrBar, CT_LABEL)
        l:SetAnchor(anchorPoint, anchorCtrl, toPoint, x, 0)
        l:SetDimensions(width, 20)
        l:SetFont("ZoFontGameSmall")
        l:SetColor(0.70, 0.68, 0.60, 1)
        l:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        l:SetText(text)
        return l
    end
    local hRank   = MakeColHdr("HRank",   hdrBar,  TOPLEFT, TOPLEFT, 10,  150, "RANK (#)")
    local hVIP    = MakeColHdr("HVIP",    hRank,   LEFT,    RIGHT,   8,   74,  "VIP / IGNORE")
    local hSales  = MakeColHdr("HSales",  hVIP,    LEFT,    RIGHT,   8,   116, "MIN SALES (GOLD)")
    local hCount  = MakeColHdr("HCount",  hSales,  LEFT,    RIGHT,   8,   76,  "COUNT (#)")
    local hDues   = MakeColHdr("HDues",   hCount,  LEFT,    RIGHT,   8,   110, "MIN DUES (GOLD)")
    local hMode   = MakeColHdr("HMode",   hDues,   LEFT,    RIGHT,   8,   76,  "MODE")
    local hStatus = MakeColHdr("HStatus", hMode,   LEFT,    RIGHT,   8,   150, "STATUS / NOTES")

    -- Rank Configuration Rows (Up to 10 ranks supported)
    self.rankDrawerRows = {}
    local rowStartY = 74

    for r = 1, 10 do
        local rCtrl = wm:CreateControl("$(parent)_RRow_" .. r, drawer, CT_BACKDROP)
        rCtrl:SetAnchor(TOPLEFT, drawer, TOPLEFT, 16, rowStartY + (r - 1) * 34)
        rCtrl:SetAnchor(TOPRIGHT, drawer, TOPRIGHT, -16, rowStartY + (r - 1) * 34)
        rCtrl:SetHeight(30)
        rCtrl:SetCenterColor(0.06, 0.06, 0.09, 0.70)
        rCtrl:SetEdgeColor(0.20, 0.20, 0.25, 0.50)
        rCtrl:SetEdgeTexture("", 8, 1, 0)

        local rNameLbl = wm:CreateControl("$(parent)_Name", rCtrl, CT_LABEL)
        rNameLbl:SetAnchor(LEFT, rCtrl, LEFT, 10, 0)
        rNameLbl:SetDimensions(150, 22)
        rNameLbl:SetFont("ZoFontGameBold")
        rNameLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        rCtrl.nameLbl = rNameLbl

        -- Ignore / VIP Toggle Button
        local ignoreBtn = wm:CreateControl("$(parent)_IgnoreBtn", rCtrl, CT_BUTTON)
        ignoreBtn:SetAnchor(LEFT, rNameLbl, RIGHT, 8, 0)
        ignoreBtn:SetDimensions(74, 22)
        ignoreBtn:SetFont("ZoFontGameSmall")
        ignoreBtn:SetText("Active")
        self:StyleTactileButton(ignoreBtn, {
            normalBg = { 0.08, 0.08, 0.12, 0.85 },
            hoverBg = { 0.14, 0.14, 0.20, 0.95 },
            normalEdge = { 0.30, 0.30, 0.35, 0.65 },
            tooltipTitle = "Toggle Rank Ignore / VIP",
            tooltipText = "When Ignored (VIP), members in this rank will never be demoted, and no members will be auto-promoted into this rank.",
        })
        rCtrl.ignoreBtn = ignoreBtn

        -- Sales Stepper & EditBox
        local sDecBtn = wm:CreateControl("$(parent)_SDec", rCtrl, CT_BUTTON)
        sDecBtn:SetAnchor(LEFT, ignoreBtn, RIGHT, 8, 0)
        sDecBtn:SetDimensions(18, 22)
        sDecBtn:SetFont("ZoFontGameBold")
        sDecBtn:SetText("<")
        self:StyleTactileButton(sDecBtn, { normalBg = { 0.10, 0.10, 0.15, 0.80 } })

        local sEditBg = wm:CreateControlFromVirtual("$(parent)_SEditBg", rCtrl, "ZO_EditBackdrop")
        sEditBg:SetAnchor(LEFT, sDecBtn, RIGHT, 4, 0)
        sEditBg:SetDimensions(72, 22)
        local sEdit = wm:CreateControlFromVirtual("$(parent)_SEdit", sEditBg, "ZO_DefaultEditForBackdrop")
        sEdit:SetAnchorFill()
        sEdit:SetFont("ZoFontGameSmall")
        sEdit:SetColor(1, 0.85, 0.2, 1)
        sEdit:SetTextType(TEXT_TYPE_NUMERIC)
        sEdit:SetMaxInputChars(9)
        rCtrl.sEdit = sEdit
        rCtrl.sEditBg = sEditBg

        local sIncBtn = wm:CreateControl("$(parent)_SInc", rCtrl, CT_BUTTON)
        sIncBtn:SetAnchor(LEFT, sEditBg, RIGHT, 4, 0)
        sIncBtn:SetDimensions(18, 22)
        sIncBtn:SetFont("ZoFontGameBold")
        sIncBtn:SetText(">")
        self:StyleTactileButton(sIncBtn, { normalBg = { 0.10, 0.10, 0.15, 0.80 } })

        -- Sales Count Stepper & EditBox
        local cDecBtn = wm:CreateControl("$(parent)_CDec", rCtrl, CT_BUTTON)
        cDecBtn:SetAnchor(LEFT, sIncBtn, RIGHT, 8, 0)
        cDecBtn:SetDimensions(18, 22)
        cDecBtn:SetFont("ZoFontGameBold")
        cDecBtn:SetText("<")
        self:StyleTactileButton(cDecBtn, { normalBg = { 0.10, 0.10, 0.15, 0.80 } })

        local cEditBg = wm:CreateControlFromVirtual("$(parent)_CEditBg", rCtrl, "ZO_EditBackdrop")
        cEditBg:SetAnchor(LEFT, cDecBtn, RIGHT, 4, 0)
        cEditBg:SetDimensions(32, 22)
        local cEdit = wm:CreateControlFromVirtual("$(parent)_CEdit", cEditBg, "ZO_DefaultEditForBackdrop")
        cEdit:SetAnchorFill()
        cEdit:SetFont("ZoFontGameSmall")
        cEdit:SetColor(0.2, 0.9, 1.0, 1)
        cEdit:SetTextType(TEXT_TYPE_NUMERIC)
        cEdit:SetMaxInputChars(4)
        rCtrl.cEdit = cEdit
        rCtrl.cEditBg = cEditBg

        local cIncBtn = wm:CreateControl("$(parent)_CInc", rCtrl, CT_BUTTON)
        cIncBtn:SetAnchor(LEFT, cEditBg, RIGHT, 4, 0)
        cIncBtn:SetDimensions(18, 22)
        cIncBtn:SetFont("ZoFontGameBold")
        cIncBtn:SetText(">")
        self:StyleTactileButton(cIncBtn, { normalBg = { 0.10, 0.10, 0.15, 0.80 } })

        -- Dues Stepper & EditBox
        local dDecBtn = wm:CreateControl("$(parent)_DDec", rCtrl, CT_BUTTON)
        dDecBtn:SetAnchor(LEFT, cIncBtn, RIGHT, 8, 0)
        dDecBtn:SetDimensions(18, 22)
        dDecBtn:SetFont("ZoFontGameBold")
        dDecBtn:SetText("<")
        self:StyleTactileButton(dDecBtn, { normalBg = { 0.10, 0.10, 0.15, 0.80 } })

        local dEditBg = wm:CreateControlFromVirtual("$(parent)_DEditBg", rCtrl, "ZO_EditBackdrop")
        dEditBg:SetAnchor(LEFT, dDecBtn, RIGHT, 4, 0)
        dEditBg:SetDimensions(66, 22)
        local dEdit = wm:CreateControlFromVirtual("$(parent)_DEdit", dEditBg, "ZO_DefaultEditForBackdrop")
        dEdit:SetAnchorFill()
        dEdit:SetFont("ZoFontGameSmall")
        dEdit:SetColor(0.35, 0.88, 0.55, 1)
        dEdit:SetTextType(TEXT_TYPE_NUMERIC)
        dEdit:SetMaxInputChars(9)
        rCtrl.dEdit = dEdit
        rCtrl.dEditBg = dEditBg

        local dIncBtn = wm:CreateControl("$(parent)_DInc", rCtrl, CT_BUTTON)
        dIncBtn:SetAnchor(LEFT, dEditBg, RIGHT, 4, 0)
        dIncBtn:SetDimensions(18, 22)
        dIncBtn:SetFont("ZoFontGameBold")
        dIncBtn:SetText(">")
        self:StyleTactileButton(dIncBtn, { normalBg = { 0.10, 0.10, 0.15, 0.80 } })

        -- Mode Button
        local modeBtn = wm:CreateControl("$(parent)_ModeBtn", rCtrl, CT_BUTTON)
        modeBtn:SetAnchor(LEFT, dIncBtn, RIGHT, 8, 0)
        modeBtn:SetDimensions(76, 22)
        modeBtn:SetFont("ZoFontGameSmall")
        modeBtn:SetText("Mode: OR")
        self:StyleTactileButton(modeBtn, {
            normalBg = { 0.08, 0.12, 0.10, 0.85 },
            hoverBg = { 0.12, 0.18, 0.15, 0.95 },
            normalEdge = { 0.20, 0.60, 0.40, 0.70 },
        })
        rCtrl.modeBtn = modeBtn

        -- Officer / Floor Status Label
        local offLbl = wm:CreateControl("$(parent)_OffLbl", rCtrl, CT_LABEL)
        offLbl:SetAnchor(LEFT, modeBtn, RIGHT, 8, 0)
        offLbl:SetDimensions(150, 22)
        offLbl:SetFont("ZoFontGameSmall")
        offLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        offLbl:SetText("")
        rCtrl.offLbl = offLbl

        rCtrl.sDecBtn = sDecBtn
        rCtrl.sIncBtn = sIncBtn
        rCtrl.cDecBtn = cDecBtn
        rCtrl.cIncBtn = cIncBtn
        rCtrl.dDecBtn = dDecBtn
        rCtrl.dIncBtn = dIncBtn

        self.rankDrawerRows[r] = rCtrl
    end

    -- Drawer Footer Action Buttons
    local resetBtn = wm:CreateControl("$(parent)_ResetBtn", drawer, CT_BUTTON)
    resetBtn:SetAnchor(BOTTOMLEFT, drawer, BOTTOMLEFT, 16, -12)
    resetBtn:SetDimensions(180, 26)
    resetBtn:SetFont("ZoFontGameSmall")
    resetBtn:SetText("Reset to Redfur Presets")
    self:StyleTactileButton(resetBtn, {
        normalBg = { 0.14, 0.08, 0.04, 0.90 },
        hoverBg = { 0.22, 0.12, 0.06, 0.98 },
        normalEdge = { 0.80, 0.45, 0.15, 0.80 },
    })
    resetBtn:SetHandler("OnClicked", function()
        local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
        self.savedVars.autoRanks.guildConfigs[tostring(gId)] = self:GetDefaultRankConfig(gId)
        self:RefreshRankConfigDrawer()
        self:EvaluateAutoRanks(gId)
        self:UpdateAutoRanksUI()
        self.PrintChat("Rank ladder reset to Redfur default presets for " .. GetGuildName(gId))
    end)

    local saveBtn = wm:CreateControl("$(parent)_SaveBtn", drawer, CT_BUTTON)
    saveBtn:SetAnchor(BOTTOMRIGHT, drawer, BOTTOMRIGHT, -16, -12)
    saveBtn:SetDimensions(160, 26)
    saveBtn:SetFont("ZoFontGameBold")
    saveBtn:SetText("Save & Re-Evaluate")
    self:StyleTactileButton(saveBtn, {
        normalBg = { 0.04, 0.16, 0.12, 0.95 },
        hoverBg = { 0.06, 0.24, 0.18, 1.00 },
        normalEdge = { 0, 0.90, 0.70, 0.95 },
        normalTextColor = { 0, 1, 0.85, 1 },
    })
    saveBtn:SetHandler("OnClicked", function()
        self:CloseRankConfigDrawer()
        local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
        self:EvaluateAutoRanks(gId)
        self:UpdateAutoRanksUI()
        self.PrintChat("Rank ladder thresholds saved! Roster re-evaluated.")
    end)
end

function FR:SetAutoRanksTableHidden(hidden)
    if self.autoRanksTitleLbl then self.autoRanksTitleLbl:SetHidden(hidden) end
    if self.autoRanksSummaryLbl then self.autoRanksSummaryLbl:SetHidden(hidden) end
    if self.autoRanksControlRow then self.autoRanksControlRow:SetHidden(hidden) end
    if self.autoRanksHeaderRow then self.autoRanksHeaderRow:SetHidden(hidden) end
    if self.autoRanksGridRows then
        for _, r in ipairs(self.autoRanksGridRows) do
            if r.row then r.row:SetHidden(hidden) end
        end
    end
    if self.autoRanksFooterLbl then self.autoRanksFooterLbl:SetHidden(hidden) end
    if self.autoRanksPageLbl then self.autoRanksPageLbl:SetHidden(hidden) end
    if self.autoRanksPrevBtn then self.autoRanksPrevBtn:SetHidden(hidden) end
    if self.autoRanksNextBtn then self.autoRanksNextBtn:SetHidden(hidden) end
end

function FR:OpenRankConfigDrawer()
    if not self.rankConfigDrawer then return end
    if self.autoWelcomeDrawer and not self.autoWelcomeDrawer:IsHidden() then
        self:CloseAutoWelcomeDrawer()
    end
    self:SetAutoRanksTableHidden(true)
    self:RefreshRankConfigDrawer()
    self.rankConfigDrawer:SetHidden(false)
end

function FR:CloseRankConfigDrawer()
    if not self.rankConfigDrawer then return end
    self.rankConfigDrawer:SetHidden(true)
    if not self.autoWelcomeDrawer or self.autoWelcomeDrawer:IsHidden() then
        self:SetAutoRanksTableHidden(false)
        self:RenderAutoRanksRows()
    end
end

function FR:ToggleRankConfigDrawer()
    if not self.rankConfigDrawer then return end
    if self.rankConfigDrawer:IsHidden() then
        self:OpenRankConfigDrawer()
    else
        self:CloseRankConfigDrawer()
    end
end

function FR:RefreshRankConfigDrawer()
    local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
    local gConfig = self:GetGuildRankConfig(gId)
    local numRanks = (gId and GetNumGuildRanks and GetNumGuildRanks(gId)) or 10
    if not numRanks or numRanks < 1 then numRanks = 10 end

    if not gConfig or not gConfig.ranks then
        gConfig = self:GetDefaultRankConfig(gId)
    end

    for r = 1, #self.rankDrawerRows do
        local rCtrl = self.rankDrawerRows[r]
        if r <= numRanks then
            rCtrl:SetHidden(false)
            local rCfg = (gConfig.ranks and gConfig.ranks[r]) or { name = (GetFinalGuildRankName and GetFinalGuildRankName(gId, r)) or string.format("Rank %d", r), minSales = 0, minSalesCount = 0, minDues = 0, mode = "OR", isOfficer = (r <= 2), isIgnored = false }
            local isOfficer = rCfg.isOfficer or self:IsOfficerCapableRank(gId, r) or (r <= 2)
            local isIgnored = rCfg.isIgnored and not isOfficer

            local rankDisplayName = rCfg.name or (GetFinalGuildRankName and GetFinalGuildRankName(gId, r)) or string.format("Rank %d", r)
            rCtrl.nameLbl:SetText(string.format("|cFFFFFF#%d: %s|r", r, rankDisplayName))

            if isOfficer then
                rCtrl.ignoreBtn:SetText("|c59E08A🛡 Officer|r")
                rCtrl.ignoreBtn:SetEnabled(false)
                self:UpdateTactileTheme(rCtrl.ignoreBtn, {
                    normalBg = { 0.04, 0.14, 0.08, 0.85 },
                    normalEdge = { 0.20, 0.70, 0.35, 0.70 },
                    normalTextColor = { 0.4, 0.9, 0.5, 1 },
                })

                rCtrl.sDecBtn:SetHidden(true)
                rCtrl.sEditBg:SetHidden(true)
                rCtrl.sIncBtn:SetHidden(true)
                rCtrl.cDecBtn:SetHidden(true)
                rCtrl.cEditBg:SetHidden(true)
                rCtrl.cIncBtn:SetHidden(true)
                rCtrl.dDecBtn:SetHidden(true)
                rCtrl.dEditBg:SetHidden(true)
                rCtrl.dIncBtn:SetHidden(true)
                rCtrl.modeBtn:SetHidden(true)
                rCtrl.offLbl:SetText("|c59E08A🛡 Officer Immunity|r")
            elseif isIgnored then
                rCtrl.ignoreBtn:SetText("|cFFD700⭐ Ignored|r")
                rCtrl.ignoreBtn:SetEnabled(true)
                self:UpdateTactileTheme(rCtrl.ignoreBtn, {
                    normalBg = { 0.20, 0.16, 0.04, 0.95 },
                    hoverBg = { 0.28, 0.22, 0.06, 1.00 },
                    normalEdge = { 0.95, 0.75, 0.20, 0.95 },
                    normalTextColor = { 1, 0.85, 0.20, 1 },
                })

                rCtrl.sDecBtn:SetHidden(true)
                rCtrl.sEditBg:SetHidden(true)
                rCtrl.sIncBtn:SetHidden(true)
                rCtrl.cDecBtn:SetHidden(true)
                rCtrl.cEditBg:SetHidden(true)
                rCtrl.cIncBtn:SetHidden(true)
                rCtrl.dDecBtn:SetHidden(true)
                rCtrl.dEditBg:SetHidden(true)
                rCtrl.dIncBtn:SetHidden(true)
                rCtrl.modeBtn:SetHidden(true)
                rCtrl.offLbl:SetText("|cFFD700⭐ VIP / Protected Rank|r")

                rCtrl.ignoreBtn:SetHandler("OnClicked", function()
                    rCfg.isIgnored = false
                    self:RefreshRankConfigDrawer()
                end)
            else
                rCtrl.ignoreBtn:SetText("|c888888Active|r")
                rCtrl.ignoreBtn:SetEnabled(true)
                self:UpdateTactileTheme(rCtrl.ignoreBtn, {
                    normalBg = { 0.08, 0.08, 0.12, 0.85 },
                    hoverBg = { 0.14, 0.14, 0.20, 0.95 },
                    normalEdge = { 0.30, 0.30, 0.35, 0.65 },
                    normalTextColor = { 0.7, 0.7, 0.7, 1 },
                })

                rCtrl.sDecBtn:SetHidden(false)
                rCtrl.sEditBg:SetHidden(false)
                rCtrl.sIncBtn:SetHidden(false)
                rCtrl.cDecBtn:SetHidden(false)
                rCtrl.cEditBg:SetHidden(false)
                rCtrl.cIncBtn:SetHidden(false)
                rCtrl.dDecBtn:SetHidden(false)
                rCtrl.dEditBg:SetHidden(false)
                rCtrl.dIncBtn:SetHidden(false)
                rCtrl.modeBtn:SetHidden(false)

                -- Populate Edit Boxes
                rCtrl.sEdit:SetText(tostring(rCfg.minSales or 0))
                rCtrl.cEdit:SetText(tostring(rCfg.minSalesCount or 0))
                rCtrl.dEdit:SetText(tostring(rCfg.minDues or 0))
                rCtrl.modeBtn:SetText("Mode: " .. (rCfg.mode or "OR"))

                if (rCfg.minSales or 0) == 0 and (rCfg.minSalesCount or 0) == 0 and (rCfg.minDues or 0) == 0 then
                    rCtrl.offLbl:SetText("|cAAAAAA[Floor / Delinquent]|r")
                else
                    rCtrl.offLbl:SetText("")
                end

                -- Wire editbox lost focus / enter
                rCtrl.sEdit:SetHandler("OnFocusLost", function(edit)
                    local val = tonumber(edit:GetText()) or 0
                    rCfg.minSales = val
                    edit:SetText(tostring(val))
                    if (rCfg.minSales or 0) == 0 and (rCfg.minSalesCount or 0) == 0 and (rCfg.minDues or 0) == 0 then
                        rCtrl.offLbl:SetText("|cAAAAAA[Floor / Delinquent]|r")
                    else
                        rCtrl.offLbl:SetText("")
                    end
                end)
                rCtrl.sEdit:SetHandler("OnEnter", function(edit) edit:LoseFocus() end)

                rCtrl.cEdit:SetHandler("OnFocusLost", function(edit)
                    local val = tonumber(edit:GetText()) or 0
                    rCfg.minSalesCount = val
                    edit:SetText(tostring(val))
                    if (rCfg.minSales or 0) == 0 and (rCfg.minSalesCount or 0) == 0 and (rCfg.minDues or 0) == 0 then
                        rCtrl.offLbl:SetText("|cAAAAAA[Floor / Delinquent]|r")
                    else
                        rCtrl.offLbl:SetText("")
                    end
                end)
                rCtrl.cEdit:SetHandler("OnEnter", function(edit) edit:LoseFocus() end)

                rCtrl.dEdit:SetHandler("OnFocusLost", function(edit)
                    local val = tonumber(edit:GetText()) or 0
                    rCfg.minDues = val
                    edit:SetText(tostring(val))
                    if (rCfg.minSales or 0) == 0 and (rCfg.minSalesCount or 0) == 0 and (rCfg.minDues or 0) == 0 then
                        rCtrl.offLbl:SetText("|cAAAAAA[Floor / Delinquent]|r")
                    else
                        rCtrl.offLbl:SetText("")
                    end
                end)
                rCtrl.dEdit:SetHandler("OnEnter", function(edit) edit:LoseFocus() end)

                -- Stepper handlers
                rCtrl.sDecBtn:SetHandler("OnClicked", function()
                    local curS = rCfg.minSales or 0
                    local newS = curS
                    for idx = #SALES_STEPS, 1, -1 do
                        if SALES_STEPS[idx] < curS then
                            newS = SALES_STEPS[idx]
                            break
                        end
                    end
                    rCfg.minSales = newS
                    rCtrl.sEdit:SetText(tostring(newS))
                    if (rCfg.minSales or 0) == 0 and (rCfg.minSalesCount or 0) == 0 and (rCfg.minDues or 0) == 0 then
                        rCtrl.offLbl:SetText("|cAAAAAA[Floor / Delinquent]|r")
                    else
                        rCtrl.offLbl:SetText("")
                    end
                end)

                rCtrl.sIncBtn:SetHandler("OnClicked", function()
                    local curS = rCfg.minSales or 0
                    local newS = curS
                    for idx = 1, #SALES_STEPS do
                        if SALES_STEPS[idx] > curS then
                            newS = SALES_STEPS[idx]
                            break
                        end
                    end
                    rCfg.minSales = newS
                    rCtrl.sEdit:SetText(tostring(newS))
                    if (rCfg.minSales or 0) == 0 and (rCfg.minSalesCount or 0) == 0 and (rCfg.minDues or 0) == 0 then
                        rCtrl.offLbl:SetText("|cAAAAAA[Floor / Delinquent]|r")
                    else
                        rCtrl.offLbl:SetText("")
                    end
                end)

                rCtrl.cDecBtn:SetHandler("OnClicked", function()
                    local curC = rCfg.minSalesCount or 0
                    local newC = curC
                    for idx = #COUNT_STEPS, 1, -1 do
                        if COUNT_STEPS[idx] < curC then
                            newC = COUNT_STEPS[idx]
                            break
                        end
                    end
                    rCfg.minSalesCount = newC
                    rCtrl.cEdit:SetText(tostring(newC))
                    if (rCfg.minSales or 0) == 0 and (rCfg.minSalesCount or 0) == 0 and (rCfg.minDues or 0) == 0 then
                        rCtrl.offLbl:SetText("|cAAAAAA[Floor / Delinquent]|r")
                    else
                        rCtrl.offLbl:SetText("")
                    end
                end)

                rCtrl.cIncBtn:SetHandler("OnClicked", function()
                    local curC = rCfg.minSalesCount or 0
                    local newC = curC
                    for idx = 1, #COUNT_STEPS do
                        if COUNT_STEPS[idx] > curC then
                            newC = COUNT_STEPS[idx]
                            break
                        end
                    end
                    rCfg.minSalesCount = newC
                    rCtrl.cEdit:SetText(tostring(newC))
                    if (rCfg.minSales or 0) == 0 and (rCfg.minSalesCount or 0) == 0 and (rCfg.minDues or 0) == 0 then
                        rCtrl.offLbl:SetText("|cAAAAAA[Floor / Delinquent]|r")
                    else
                        rCtrl.offLbl:SetText("")
                    end
                end)

                rCtrl.dDecBtn:SetHandler("OnClicked", function()
                    local curD = rCfg.minDues or 0
                    local newD = curD
                    for idx = #DUES_STEPS, 1, -1 do
                        if DUES_STEPS[idx] < curD then
                            newD = DUES_STEPS[idx]
                            break
                        end
                    end
                    rCfg.minDues = newD
                    rCtrl.dEdit:SetText(tostring(newD))
                    if (rCfg.minSales or 0) == 0 and (rCfg.minSalesCount or 0) == 0 and (rCfg.minDues or 0) == 0 then
                        rCtrl.offLbl:SetText("|cAAAAAA[Floor / Delinquent]|r")
                    else
                        rCtrl.offLbl:SetText("")
                    end
                end)

                rCtrl.dIncBtn:SetHandler("OnClicked", function()
                    local curD = rCfg.minDues or 0
                    local newD = curD
                    for idx = 1, #DUES_STEPS do
                        if DUES_STEPS[idx] > curD then
                            newD = DUES_STEPS[idx]
                            break
                        end
                    end
                    rCfg.minDues = newD
                    rCtrl.dEdit:SetText(tostring(newD))
                    if (rCfg.minSales or 0) == 0 and (rCfg.minSalesCount or 0) == 0 and (rCfg.minDues or 0) == 0 then
                        rCtrl.offLbl:SetText("|cAAAAAA[Floor / Delinquent]|r")
                    else
                        rCtrl.offLbl:SetText("")
                    end
                end)

                rCtrl.modeBtn:SetHandler("OnClicked", function()
                    local curM = rCfg.mode or "OR"
                    local nextM = (curM == "OR" and "SUM") or (curM == "SUM" and "AND") or "OR"
                    rCfg.mode = nextM
                    rCtrl.modeBtn:SetText("Mode: " .. nextM)
                end)

                rCtrl.ignoreBtn:SetHandler("OnClicked", function()
                    rCfg.isIgnored = true
                    self:RefreshRankConfigDrawer()
                end)
            end
        else
            rCtrl:SetHidden(true)
        end
    end
end

--[[ =========================================================================
     RENDERING & FILTERING
========================================================================= ]]--

function FR:UpdateAutoRanksFilterButtons()
    for id, btn in pairs(self.autoRankFilterBtns or {}) do
        if btn.bg then
            if id == self.autoRankFilter then
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

function FR:RefreshAutoRanksGrid()
    local filter = self.autoRankFilter or "all"
    local filtered = {}

    local pCount = 0
    local dCount = 0
    local kCount = 0
    local xCount = 0

    for _, item in ipairs(self.autoRankResults or {}) do
        if item.isExempt then xCount = xCount + 1 end
        if item.action == "PROMOTE" then pCount = pCount + 1
        elseif item.action == "DEMOTE" then dCount = dCount + 1
        else kCount = kCount + 1 end

        local match = false
        if filter == "all" then match = true
        elseif filter == "changes" and (item.action == "PROMOTE" or item.action == "DEMOTE") then match = true
        elseif filter == "promote" and item.action == "PROMOTE" then match = true
        elseif filter == "demote" and item.action == "DEMOTE" then match = true
        elseif filter == "exempt" and item.isExempt then match = true
        end

        if match then table.insert(filtered, item) end
    end

    self.autoRankFilteredResults = filtered

    if self.autoRanksSummaryLbl then
        self.autoRanksSummaryLbl:SetText(string.format("Evaluated: |cFFFFFF%d|r  |  |t12:12:EsoUI/Art/Buttons/pointsplus_up.dds|t |c59E08A%d|r  |  |t12:12:EsoUI/Art/Buttons/pointsplus_down.dds|t |cFF6666%d|r  |  |c888888Kept: %d|r  |  |t12:12:EsoUI/Art/Campaign/overview_guildOwner_icon.dds|t |c00FFCC%d|r",
            #self.autoRankResults, pCount, dCount, kCount, xCount))
    end

    if self.autoRanksApplyBtn then
        local pending = 0
        for _, item in ipairs(self.autoRankResults or {}) do
            if item.selected and item.action ~= "KEEP" then
                pending = pending + 1
            end
        end
        self.autoRanksApplyBtn:SetText(string.format("Apply Changes (%d)", pending))
        self.autoRanksApplyBtn:SetEnabled(pending > 0)
    end

    self:RenderAutoRanksRows()
end

function FR:RenderAutoRanksRows()
    local list = self.autoRankFilteredResults or {}
    local total = #list
    local maxPages = math.max(1, math.ceil(total / ROWS_PER_PAGE))
    if self.autoRankCurrentPage > maxPages then self.autoRankCurrentPage = maxPages end
    if self.autoRankCurrentPage < 1 then self.autoRankCurrentPage = 1 end

    local startIndex = (self.autoRankCurrentPage - 1) * ROWS_PER_PAGE

    for r = 1, ROWS_PER_PAGE do
        local rCtrl = self.autoRanksGridRows[r]
        local itemIndex = startIndex + r
        local item = list[itemIndex]

        if rCtrl then
            rCtrl.row.rowIndex = r
            if item then
                rCtrl.row:SetHidden(false)
                rCtrl.row.memberData = item

                if r % 2 == 0 then
                    rCtrl.row:SetCenterColor(0.04, 0.04, 0.06, 0.60)
                else
                    rCtrl.row:SetCenterColor(0.03, 0.03, 0.04, 0.40)
                end

                if item.action == "KEEP" or item.isExempt then
                    rCtrl.check:SetHidden(true)
                else
                    rCtrl.check:SetHidden(false)
                    rCtrl.check:SetText(item.selected and "|c00FFCC[X]|r" or "|c555555[ ]|r")
                    rCtrl.check:SetHandler("OnClicked", function()
                        item.selected = not item.selected
                        self:RefreshAutoRanksGrid()
                    end)
                end

                rCtrl.member:SetText(string.format("|cFFFFFF%s|r", item.displayName))
                rCtrl.curRank:SetText(string.format("|cCCCCCC%s|r", item.currentRankName))

                local tgtColor = (item.action == "PROMOTE" and "|t14:14:EsoUI/Art/Buttons/pointsplus_up.dds|t |c59E08A")
                    or (item.action == "DEMOTE" and "|t14:14:EsoUI/Art/Buttons/pointsplus_down.dds|t |cFF6666")
                    or "|c888888"
                rCtrl.tgtRank:SetText(string.format("%s%s|r", tgtColor, item.targetRankName))

                local actBadge = "|c888888KEPT|r"
                if item.isExempt then
                    actBadge = "|t14:14:EsoUI/Art/Campaign/overview_guildOwner_icon.dds|t |c00FFCCEXEMPT|r"
                elseif item.action == "PROMOTE" then
                    actBadge = "|t14:14:EsoUI/Art/Buttons/pointsplus_up.dds|t |c59E08APROMOTE|r"
                elseif item.action == "DEMOTE" then
                    actBadge = "|t14:14:EsoUI/Art/Buttons/pointsplus_down.dds|t |cFF6666DEMOTE|r"
                end
                rCtrl.action:SetText(actBadge)

                rCtrl.sales:SetText(item.salesGold > 0 and string.format("|cFFD700%sg|r", ZO_LocalizeDecimalNumber(item.salesGold)) or "|c555555--|r")
                rCtrl.dep:SetText(item.donations > 0 and string.format("|c59E08A%sg|r", ZO_LocalizeDecimalNumber(item.donations)) or "|c555555--|r")

                local rawStatus = item.status or ""
                local compactStatus = "|c888888Base Rank|r"
                if item.isExempt then
                    compactStatus = "|cE6C387Shielded|r"
                elseif string.find(rawStatus, "Probation") then
                    compactStatus = "|c00FFCCProbation|r"
                elseif string.find(rawStatus, "Qualified") then
                    if item.salesGold > 0 and item.donations > 0 then
                        compactStatus = "|c59E08AMet (Sales & Dues)|r"
                    elseif item.salesGold > 0 then
                        compactStatus = "|cFFD700Met (Sales)|r"
                    else
                        compactStatus = "|c59E08AMet (Dues)|r"
                    end
                elseif string.find(rawStatus, "Missing") or string.find(rawStatus, "Demote") or item.action == "DEMOTE" then
                    compactStatus = "|cFF6666Missing Dues|r"
                elseif item.action == "PROMOTE" then
                    compactStatus = "|c59E08APromotion Target|r"
                elseif rawStatus ~= "" then
                    compactStatus = string.format("|c999999%s|r", rawStatus)
                end
                rCtrl.note:SetText(compactStatus)
            else
                rCtrl.row:SetHidden(true)
                rCtrl.row.memberData = nil
            end
        end
    end

    if self.autoRanksPageLbl then
        self.autoRanksPageLbl:SetText(string.format("Page %d/%d (%d members)", self.autoRankCurrentPage, maxPages, total))
    end

    if self.autoRanksFooterLbl then
        self.autoRanksFooterLbl:SetText(string.format("Roster: %d members  •  Scroll wheel to page", total))
    end
end

function FR:UpdateAutoRanksUI()
    if (self.rankConfigDrawer and not self.rankConfigDrawer:IsHidden()) or
       (self.autoWelcomeDrawer and not self.autoWelcomeDrawer:IsHidden()) then
        return
    end
    local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
    if not self.autoRankResults or #self.autoRankResults == 0 then
        self:EvaluateAutoRanks(gId)
    end
    if self.autoRanksWindowBtn then
        self.autoRanksWindowBtn:SetText((self.autoRankLookbackDays or 10) .. " Days")
    end
    self:UpdateAutoRanksFilterButtons()
    self:RefreshAutoRanksGrid()
    self:UpdateAutoWelcomeButtonBadge()
end

--[[ =========================================================================
     SLASH COMMANDS & NAVIGATION
========================================================================= ]]--

function FR:OpenAutoRanksConsole()
    if self.ToggleConsole then
        self:ToggleConsole(true)
    end
    if self.SelectConsoleTab then
        self:SelectConsoleTab(6)
    end
end

SLASH_COMMANDS["/autoranks"] = function() FR:OpenAutoRanksConsole() end
SLASH_COMMANDS["/ar"] = function() FR:OpenAutoRanksConsole() end

--[[ =========================================================================
     AUTOWELCOME RECRUIT ENGINE & LETTER STUDIO MODAL
========================================================================= ]]--

function FR:EnsureAutoWelcomeState()
    if not self.savedVars then return end
    if not self.savedVars.autoWelcome then
        self.savedVars.autoWelcome = {
            welcomed = {},
            pending = {},
            guildSettings = {},
            autoSendWhenMailOpen = false,
            initialized = false,
            mailDelay = 2500,
        }
    end
    local aw = self.savedVars.autoWelcome
    if aw.welcomed == nil then aw.welcomed = {} end
    if aw.pending == nil then aw.pending = {} end
    if aw.guildSettings == nil then aw.guildSettings = {} end
    if aw.autoSendWhenMailOpen == nil then aw.autoSendWhenMailOpen = false end
    if aw.mailDelay == nil then aw.mailDelay = 2500 end
end

function FR:GetAutoWelcomePendingCount(guildId)
    if not self.savedVars or not self.savedVars.autoWelcome then return 0 end
    local aw = self.savedVars.autoWelcome
    if not aw.pending or not aw.pending[guildId] then return 0 end
    local count = 0
    for _ in pairs(aw.pending[guildId]) do
        count = count + 1
    end
    return count
end

function FR:UpdateAutoWelcomeButtonBadge()
    if not self.autoRanksWelcomeBtn then return end
    local gIdx = self.selectedGuildIndex or 1
    local guildId = self:ResolveGuildId(gIdx)
    local count = self:GetAutoWelcomePendingCount(guildId)
    if count > 0 then
        self.autoRanksWelcomeBtn:SetText(string.format("✉ Welcome (%d)", count))
        self:UpdateTactileTheme(self.autoRanksWelcomeBtn, {
            normalBg = { 0.16, 0.10, 0.04, 0.90 },
            hoverBg = { 0.24, 0.15, 0.06, 0.98 },
            normalEdge = { 0.90, 0.60, 0.15, 0.90 },
            hoverEdge = { 1.00, 0.80, 0.20, 1.00 },
            normalTextColor = { 1, 0.85, 0.20, 1 },
        })
    else
        self.autoRanksWelcomeBtn:SetText("✉ Auto-Welcome")
        self:UpdateTactileTheme(self.autoRanksWelcomeBtn, {
            normalBg = { 0.08, 0.08, 0.12, 0.85 },
            hoverBg = { 0.12, 0.18, 0.22, 0.95 },
            normalEdge = { 0.30, 0.30, 0.35, 0.65 },
            hoverEdge = { 0, 0.85, 0.75, 1.0 },
            normalTextColor = { 0.7, 0.8, 0.85, 1 },
        })
    end
end

function FR:InterpolateWelcomeLetter(template, guildId, memberName)
    if not template or template == "" then return "" end
    local guildName = GetGuildName(guildId) or "our guild"
    local kiosk = "Guild Kiosk"
    if GetGuildKiosk and GetGuildKiosk(guildId) then
        local kName = GetGuildKiosk(guildId)
        if kName and kName ~= "" then kiosk = kName end
    end

    local rafflePot = "Active"
    if self.savedVars and self.savedVars.raffle and self.savedVars.raffle.pot then
        rafflePot = ZO_LocalizeDecimalNumber(self.savedVars.raffle.pot)
    end

    local discord = "discord.gg/redfur"
    if self.savedVars and self.savedVars.settings and self.savedVars.settings.discordLink then
        discord = self.savedVars.settings.discordLink
    end

    local text = template
    text = string.gsub(text, "{name}", memberName or "@Recruit")
    text = string.gsub(text, "{guild_name}", guildName)
    text = string.gsub(text, "{kiosk}", kiosk)
    text = string.gsub(text, "{kiosk_location}", kiosk)
    text = string.gsub(text, "{raffle_pot}", rafflePot)
    text = string.gsub(text, "{discord}", discord)
    return text
end

function FR:BuildAutoWelcomeDrawer(parent)
    local wm = WINDOW_MANAGER
    local drawer = wm:CreateControl("$(parent)_WelcomeDrawer", parent, CT_CONTROL)
    drawer:SetAnchor(TOPLEFT, parent, TOPLEFT, 4, 4)
    drawer:SetAnchor(BOTTOMRIGHT, parent, BOTTOMRIGHT, -4, -4)
    drawer:SetMouseEnabled(true)
    drawer:SetHandler("OnMouseWheel", function() end)
    drawer:SetHidden(true)
    self.autoWelcomeDrawer = drawer

    -- Dedicated opaque plate guaranteeing zero label or texture bleed-through
    local bg = wm:CreateControl("$(parent)_Bg", drawer, CT_BACKDROP)
    bg:SetAnchorFill()
    bg:SetCenterColor(0.04, 0.04, 0.07, 1.0)
    bg:SetEdgeColor(0.0, 0.85, 0.75, 0.95)
    bg:SetEdgeTexture("", 8, 1, 0)
    bg:SetDrawLayer(DL_BACKGROUND)
    bg:SetDrawLevel(0)

    local defBg = wm:CreateControlFromVirtual("$(parent)_DefBg", drawer, "ZO_DefaultBackdrop")
    defBg:SetAnchorFill()
    defBg:SetAlpha(1.0)
    defBg:SetDrawLayer(DL_BACKGROUND)
    defBg:SetDrawLevel(1)

    local munge = wm:CreateControl("$(parent)_Munge", drawer, CT_TEXTURE)
    munge:SetAnchorFill()
    munge:SetTexture("EsoUI/Art/Performance/StatusMeterMunge.dds")
    munge:SetColor(0.05, 0.04, 0.08, 0.98)
    munge:SetDrawLayer(DL_BACKGROUND)
    munge:SetDrawLevel(2)

    -- Title & Subtitle
    local titleLbl = wm:CreateControl("$(parent)_Title", drawer, CT_LABEL)
    dTitle = titleLbl
    titleLbl:SetAnchor(TOPLEFT, drawer, TOPLEFT, 16, 12)
    titleLbl:SetFont("ZoFontGameBold")
    titleLbl:SetText("|cFF9900AUTO-WELCOME RECRUIT ENGINE|r • |c00FFCCOnboarding & Letter Studio|r")

    local subLbl = wm:CreateControl("$(parent)_Subtitle", drawer, CT_LABEL)
    subLbl:SetAnchor(TOPLEFT, titleLbl, BOTTOMLEFT, 0, 4)
    subLbl:SetFont("ZoFontGameSmall")
    subLbl:SetText("|cAAAAAAMonitor newly joined recruits, customize onboarding letters, and dispatch welcome mails.|r")

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
        tooltipTitle = "Close AutoWelcome Studio",
    })
    closeBtn:SetHandler("OnClicked", function()
        self:CloseAutoWelcomeDrawer()
    end)

    -- LEFT COLUMN: Pending Recruits List (Width 360)
    local leftCol = wm:CreateControl("$(parent)_LeftCol", drawer, CT_CONTROL)
    leftCol:SetAnchor(TOPLEFT, drawer, TOPLEFT, 14, 52)
    leftCol:SetDimensions(360, 430)

    local pendingHdr = wm:CreateControl("$(parent)_PendingHdr", leftCol, CT_LABEL)
    pendingHdr:SetAnchor(TOPLEFT, leftCol, TOPLEFT, 0, 0)
    pendingHdr:SetFont("ZoFontGameBold")
    pendingHdr:SetText("Pending Recruits (0)")
    self.autoWelcomePendingHdr = pendingHdr

    -- Recruits List Box
    local listCard = wm:CreateControl("$(parent)_ListCard", leftCol, CT_BACKDROP)
    listCard:SetAnchor(TOPLEFT, pendingHdr, BOTTOMLEFT, 0, 6)
    listCard:SetDimensions(360, 310)
    listCard:SetCenterColor(0.03, 0.03, 0.05, 0.85)
    listCard:SetEdgeColor(0.25, 0.25, 0.30, 0.50)
    listCard:SetEdgeTexture("", 8, 1, 0)

    self.autoWelcomeRecruitRows = {}
    local RECRUIT_ROWS = 9
    for i = 1, RECRUIT_ROWS do
        local r = wm:CreateControl("$(parent)_R_" .. i, listCard, CT_BACKDROP)
        r:SetAnchor(TOPLEFT, listCard, TOPLEFT, 6, 6 + (i - 1) * 33)
        r:SetAnchor(TOPRIGHT, listCard, TOPRIGHT, -6, 6 + (i - 1) * 33)
        r:SetHeight(30)
        r:SetCenterColor(0.05, 0.05, 0.08, 0.60)
        r:SetEdgeColor(0.20, 0.18, 0.25, 0.40)
        r:SetEdgeTexture("", 8, 1, 0)

        local nameLbl = wm:CreateControl("$(parent)_Name", r, CT_LABEL)
        nameLbl:SetAnchor(LEFT, r, LEFT, 8, 0)
        nameLbl:SetDimensions(220, 20)
        nameLbl:SetFont("ZoFontGameMedium")
        nameLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)
        nameLbl:SetText("@Recruit")
        r.nameLbl = nameLbl

        local statusLbl = wm:CreateControl("$(parent)_Status", r, CT_LABEL)
        statusLbl:SetAnchor(LEFT, nameLbl, RIGHT, 4, 0)
        statusLbl:SetDimensions(75, 20)
        statusLbl:SetFont("ZoFontGameSmall")
        statusLbl:SetText("|cFFD700Pending|r")
        r.statusLbl = statusLbl

        local delBtn = wm:CreateControl("$(parent)_DelBtn", r, CT_BUTTON)
        delBtn:SetAnchor(RIGHT, r, RIGHT, -6, 0)
        delBtn:SetDimensions(22, 20)
        delBtn:SetFont("ZoFontGameBold")
        delBtn:SetText("X")
        self:StyleTactileButton(delBtn, {
            normalBg = { 0.15, 0.05, 0.05, 0.80 },
            hoverBg = { 0.25, 0.08, 0.08, 0.95 },
            normalEdge = { 0.50, 0.20, 0.20, 0.70 },
            hoverEdge = { 0.90, 0.30, 0.30, 1.00 },
            normalTextColor = { 1, 0.5, 0.5, 1 },
            hoverTextColor = { 1, 0.8, 0.8, 1 },
            tooltipTitle = "Dismiss Recruit",
            tooltipText = "Mark this recruit as welcomed without sending mail.",
        })
        r.delBtn = delBtn

        self.autoWelcomeRecruitRows[i] = r
    end

    -- Left Column Bottom Buttons: Dispatch & Mark All Welcomed
    local dispatchBtn = wm:CreateControl("$(parent)_DispatchBtn", leftCol, CT_BUTTON)
    dispatchBtn:SetAnchor(TOPLEFT, listCard, BOTTOMLEFT, 0, 10)
    dispatchBtn:SetDimensions(175, 26)
    dispatchBtn:SetFont("ZoFontGameBold")
    dispatchBtn:SetText("Dispatch Welcomes")
    self:StyleTactileButton(dispatchBtn, {
        normalBg = { 0.18, 0.12, 0.04, 0.90 },
        hoverBg = { 0.26, 0.16, 0.06, 0.98 },
        normalEdge = { 0.90, 0.60, 0.15, 0.90 },
        hoverEdge = { 1.00, 0.80, 0.20, 1.00 },
        normalTextColor = { 1, 0.85, 0.20, 1 },
        hoverTextColor = { 1, 0.95, 0.50, 1 },
        tooltipTitle = "Dispatch Welcomes",
        tooltipText = "Sequentially send personalized welcome mails to all pending recruits with safe 2.5s pacing.",
    })
    dispatchBtn:SetHandler("OnClicked", function()
        local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
        self:DispatchWelcomeMails(gId)
    end)
    self.autoWelcomeDispatchBtn = dispatchBtn

    local markAllBtn = wm:CreateControl("$(parent)_MarkAllBtn", leftCol, CT_BUTTON)
    markAllBtn:SetAnchor(TOPLEFT, dispatchBtn, TOPRIGHT, 10, 0)
    markAllBtn:SetDimensions(175, 26)
    markAllBtn:SetFont("ZoFontGameBold")
    markAllBtn:SetText("Mark All Welcomed")
    self:StyleTactileButton(markAllBtn, {
        normalBg = { 0.04, 0.14, 0.10, 0.85 },
        hoverBg = { 0.06, 0.20, 0.14, 0.95 },
        normalEdge = { 0.20, 0.70, 0.35, 0.80 },
        hoverEdge = { 0.30, 1.00, 0.50, 1.00 },
        normalTextColor = { 0.3, 1, 0.5, 1 },
        hoverTextColor = { 0.6, 1, 0.7, 1 },
        tooltipTitle = "Mark All Welcomed",
        tooltipText = "Clear all pending recruits and mark them as welcomed (prevents mailing existing roster).",
    })
    markAllBtn:SetHandler("OnClicked", function()
        local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
        self:MarkAllCurrentWelcomed(gId)
    end)
    self.autoWelcomeMarkAllBtn = markAllBtn

    local welcomeStatLbl = wm:CreateControl("$(parent)_StatLbl", leftCol, CT_LABEL)
    welcomeStatLbl:SetAnchor(TOPLEFT, dispatchBtn, BOTTOMLEFT, 2, 8)
    welcomeStatLbl:SetFont("ZoFontGameSmall")
    welcomeStatLbl:SetText("Status: Ready")
    self.autoWelcomeStatusLbl = welcomeStatLbl

    -- RIGHT COLUMN: Welcome Letter Template Editor (Width 450)
    local rightCol = wm:CreateControl("$(parent)_RightCol", drawer, CT_CONTROL)
    rightCol:SetAnchor(TOPLEFT, leftCol, TOPRIGHT, 20, 0)
    rightCol:SetAnchor(BOTTOMRIGHT, drawer, BOTTOMRIGHT, -14, -10)

    local editorHdr = wm:CreateControl("$(parent)_EditorHdr", rightCol, CT_LABEL)
    editorHdr:SetAnchor(TOPLEFT, rightCol, TOPLEFT, 0, 0)
    editorHdr:SetFont("ZoFontGameBold")
    editorHdr:SetText("Welcome Letter Template")
    self.autoWelcomeEditorHdr = editorHdr

    -- Guild Enable Toggle
    local enableBtn = wm:CreateControl("$(parent)_EnableBtn", rightCol, CT_BUTTON)
    enableBtn:SetAnchor(TOPLEFT, editorHdr, BOTTOMLEFT, 0, 8)
    enableBtn:SetDimensions(215, 22)
    enableBtn:SetFont("ZoFontGameSmall")
    enableBtn:SetText("[X] Enable for Guild")
    self:StyleTactileButton(enableBtn, {
        normalBg = { 0.05, 0.12, 0.08, 0.85 },
        hoverBg = { 0.08, 0.18, 0.12, 0.95 },
        normalEdge = { 0.20, 0.75, 0.35, 0.80 },
        hoverEdge = { 0.30, 1.00, 0.50, 1.00 },
        normalTextColor = { 0.3, 1, 0.5, 1 },
    })
    enableBtn:SetHandler("OnClicked", function()
        local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
        local aw = self.savedVars and self.savedVars.autoWelcome
        if aw then
            aw.guildSettings[gId] = aw.guildSettings[gId] or {}
            local cur = aw.guildSettings[gId].enabled
            aw.guildSettings[gId].enabled = not cur
            self:RefreshAutoWelcomeDrawer()
        end
    end)
    self.autoWelcomeEnableBtn = enableBtn

    -- Auto-Send When Mail Opens Toggle
    local autoSendBtn = wm:CreateControl("$(parent)_AutoSendBtn", rightCol, CT_BUTTON)
    autoSendBtn:SetAnchor(LEFT, enableBtn, RIGHT, 10, 0)
    autoSendBtn:SetDimensions(220, 22)
    autoSendBtn:SetFont("ZoFontGameSmall")
    autoSendBtn:SetText("[ ] Auto-Send on Mail Open")
    self:StyleTactileButton(autoSendBtn, {
        normalBg = { 0.08, 0.08, 0.12, 0.85 },
        hoverBg = { 0.12, 0.16, 0.22, 0.95 },
        normalEdge = { 0.30, 0.40, 0.55, 0.70 },
        hoverEdge = { 0.50, 0.70, 0.90, 1.00 },
        normalTextColor = { 0.7, 0.8, 0.9, 1 },
    })
    autoSendBtn:SetHandler("OnClicked", function()
        local aw = self.savedVars and self.savedVars.autoWelcome
        if aw then
            aw.autoSendWhenMailOpen = not aw.autoSendWhenMailOpen
            self:RefreshAutoWelcomeDrawer()
        end
    end)
    self.autoWelcomeAutoSendBtn = autoSendBtn

    -- Subject Field
    local subjLbl = wm:CreateControl("$(parent)_SubjLbl", rightCol, CT_LABEL)
    subjLbl:SetAnchor(TOPLEFT, enableBtn, BOTTOMLEFT, 0, 10)
    subjLbl:SetFont("ZoFontGameSmall")
    subjLbl:SetText("Mail Subject (Max 50 characters):")

    local subjBg = wm:CreateControlFromVirtual("$(parent)_SubjBg", rightCol, "ZO_EditBackdrop")
    subjBg:SetAnchor(TOPLEFT, subjLbl, BOTTOMLEFT, 0, 4)
    subjBg:SetDimensions(440, 24)

    local subjBox = wm:CreateControlFromVirtual("$(parent)_SubjBox", subjBg, "ZO_DefaultEditForBackdrop")
    subjBox:SetAnchorFill()
    subjBox:SetFont("ZoFontGameSmall")
    subjBox:SetMaxInputChars(50)
    self.autoWelcomeSubjBox = subjBox

    -- Body Field
    local bodyLbl = wm:CreateControl("$(parent)_BodyLbl", rightCol, CT_LABEL)
    bodyLbl:SetAnchor(TOPLEFT, subjBg, BOTTOMLEFT, 0, 10)
    bodyLbl:SetFont("ZoFontGameSmall")
    bodyLbl:SetText("Mail Body (Max 700 characters):")

    local bodyBg = wm:CreateControlFromVirtual("$(parent)_BodyBg", rightCol, "ZO_EditBackdrop")
    bodyBg:SetAnchor(TOPLEFT, bodyLbl, BOTTOMLEFT, 0, 4)
    bodyBg:SetDimensions(440, 175)

    local bodyBox = wm:CreateControlFromVirtual("$(parent)_BodyBox", bodyBg, "ZO_DefaultEditForBackdrop")
    bodyBox:SetAnchorFill()
    bodyBox:SetFont("ZoFontGameSmall")
    bodyBox:SetMaxInputChars(700)
    bodyBox:SetMultiLine(true)
    self.autoWelcomeBodyBox = bodyBox

    -- Token Chips Row
    local tokenLbl = wm:CreateControl("$(parent)_TokenLbl", rightCol, CT_LABEL)
    tokenLbl:SetAnchor(TOPLEFT, bodyBg, BOTTOMLEFT, 0, 8)
    tokenLbl:SetFont("ZoFontGameSmall")
    tokenLbl:SetText("Insert Token Chips:")

    local tokens = {
        { id = "{name}", label = "{name}", tip = "Inserts the recruit's account name." },
        { id = "{guild_name}", label = "{guild_name}", tip = "Inserts the active guild name." },
        { id = "{kiosk}", label = "{kiosk}", tip = "Inserts the guild's current kiosk trader location." },
        { id = "{raffle_pot}", label = "{raffle_pot}", tip = "Inserts the active weekly raffle pot amount." },
        { id = "{discord}", label = "{discord}", tip = "Inserts the guild Discord invite link." },
    }
    local tX = 0
    for _, t in ipairs(tokens) do
        local tBtn = wm:CreateControl("$(parent)_T_" .. t.label, rightCol, CT_BUTTON)
        tBtn:SetAnchor(TOPLEFT, tokenLbl, BOTTOMLEFT, tX, 4)
        tBtn:SetDimensions(82, 20)
        tBtn:SetFont("ZoFontGameSmall")
        tBtn:SetText(t.label)
        self:StyleTactileButton(tBtn, {
            normalBg = { 0.10, 0.08, 0.14, 0.85 },
            hoverBg = { 0.16, 0.12, 0.22, 0.95 },
            normalEdge = { 0.50, 0.35, 0.70, 0.70 },
            hoverEdge = { 0.80, 0.50, 1.00, 1.00 },
            normalTextColor = { 0.8, 0.7, 1, 1 },
            tooltipTitle = "Insert Token: " .. t.id,
            tooltipText = t.tip,
        })
        tBtn:SetHandler("OnClicked", function()
            local cur = bodyBox:GetText() or ""
            bodyBox:SetText(cur .. " " .. t.id)
            bodyBox:TakeFocus()
        end)
        tX = tX + 86
    end

    -- Save Letter Button
    local saveBtn = wm:CreateControl("$(parent)_SaveBtn", rightCol, CT_BUTTON)
    saveBtn:SetAnchor(BOTTOMLEFT, rightCol, BOTTOMLEFT, 0, 0)
    saveBtn:SetDimensions(150, 24)
    saveBtn:SetFont("ZoFontGameBold")
    saveBtn:SetText("Save Letter Template")
    self:StyleTactileButton(saveBtn, {
        normalBg = { 0.04, 0.14, 0.14, 0.90 },
        hoverBg = { 0.06, 0.22, 0.20, 0.98 },
        normalEdge = { 0, 0.80, 0.70, 0.85 },
        hoverEdge = { 0, 1.00, 0.90, 1.00 },
        normalTextColor = { 0, 1, 0.85, 1 },
    })
    saveBtn:SetHandler("OnClicked", function()
        local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
        local aw = self.savedVars and self.savedVars.autoWelcome
        if aw then
            aw.guildSettings[gId] = aw.guildSettings[gId] or {}
            aw.guildSettings[gId].subject = subjBox:GetText() or ""
            aw.guildSettings[gId].message = bodyBox:GetText() or ""
            self.PrintChat("|c59E08AAutoWelcome:|r Saved welcome letter template for " .. GetGuildName(gId))
        end
    end)
    self.autoWelcomeSaveBtn = saveBtn
end

function FR:OpenAutoWelcomeDrawer()
    if not self.autoWelcomeDrawer then return end
    if self.rankConfigDrawer and not self.rankConfigDrawer:IsHidden() then
        self:CloseRankConfigDrawer()
    end
    self:SetAutoRanksTableHidden(true)
    self:RefreshAutoWelcomeDrawer()
    self.autoWelcomeDrawer:SetHidden(false)
end

function FR:CloseAutoWelcomeDrawer()
    if not self.autoWelcomeDrawer then return end
    self.autoWelcomeDrawer:SetHidden(true)
    if not self.rankConfigDrawer or self.rankConfigDrawer:IsHidden() then
        self:SetAutoRanksTableHidden(false)
        self:RenderAutoRanksRows()
    end
end

function FR:ToggleAutoWelcomeDrawer()
    if not self.autoWelcomeDrawer then return end
    if self.autoWelcomeDrawer:IsHidden() then
        self:OpenAutoWelcomeDrawer()
    else
        self:CloseAutoWelcomeDrawer()
    end
end

function FR:RefreshAutoWelcomeDrawer()
    if not self.autoWelcomeDrawer then return end
    local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
    local gName = GetGuildName(gId)
    local aw = self.savedVars and self.savedVars.autoWelcome

    if self.autoWelcomeEditorHdr then
        self.autoWelcomeEditorHdr:SetText(string.format("Welcome Letter: |c00FFCC%s|r", gName))
    end

    local gCfg = aw and aw.guildSettings and aw.guildSettings[gId] or {}
    local isEnabled = gCfg.enabled == true
    if self.autoWelcomeEnableBtn then
        self.autoWelcomeEnableBtn:SetText(isEnabled and "|c59E08A[X] Enabled for Guild|r" or "|c888888[ ] Disabled for Guild|r")
    end

    if self.autoWelcomeAutoSendBtn and aw then
        self.autoWelcomeAutoSendBtn:SetText(aw.autoSendWhenMailOpen and "|c00FFCC[X] Auto-Send on Mail Open|r" or "|c888888[ ] Auto-Send on Mail Open|r")
    end

    if self.autoWelcomeSubjBox then
        self.autoWelcomeSubjBox:SetText(gCfg.subject or string.format("Welcome to %s!", gName))
    end

    if self.autoWelcomeBodyBox then
        local defBody = string.format("Greetings {name},\n\nWelcome to %s! We are thrilled to have you in our trading family.\n\nGuild Kiosk: {kiosk}\nWeekly Raffle Pot: {raffle_pot} gold\nCommunity Discord: {discord}\n\nWarm regards,\n%s Staff", gName, gName)
        self.autoWelcomeBodyBox:SetText((gCfg.message and gCfg.message ~= "") and gCfg.message or defBody)
    end

    -- Populate Pending Recruits
    local pending = (aw and aw.pending and aw.pending[gId]) or {}
    local recList = {}
    for lowerName, dispName in pairs(pending) do
        table.insert(recList, { lower = lowerName, display = dispName })
    end
    table.sort(recList, function(a, b) return a.display < b.display end)

    if self.autoWelcomePendingHdr then
        self.autoWelcomePendingHdr:SetText(string.format("Pending Recruits (|c00FFCC%d|r)", #recList))
    end

    local RECRUIT_ROWS = 9
    for i = 1, RECRUIT_ROWS do
        local r = self.autoWelcomeRecruitRows and self.autoWelcomeRecruitRows[i]
        if r then
            if i <= #recList then
                local rec = recList[i]
                r:SetHidden(false)
                r.nameLbl:SetText(rec.display)
                r.delBtn:SetHandler("OnClicked", function()
                    if aw and aw.pending and aw.pending[gId] then
                        aw.pending[gId][rec.lower] = nil
                        aw.welcomed[gId] = aw.welcomed[gId] or {}
                        aw.welcomed[gId][rec.lower] = true
                        self:RefreshAutoWelcomeDrawer()
                        self:UpdateAutoWelcomeButtonBadge()
                    end
                end)
            else
                r:SetHidden(true)
            end
        end
    end

    if self.autoWelcomeDispatchBtn then
        self.autoWelcomeDispatchBtn:SetEnabled(#recList > 0)
    end

    self:UpdateAutoWelcomeButtonBadge()
end

function FR:DispatchWelcomeMails(guildId)
    if not self.savedVars or not self.savedVars.autoWelcome then return end
    local aw = self.savedVars.autoWelcome
    local pending = aw.pending and aw.pending[guildId]
    if not pending or next(pending) == nil then
        self.PrintChat("|cFFCC00AutoWelcome:|r No pending recruits to welcome for this guild.")
        return
    end

    local gCfg = aw.guildSettings and aw.guildSettings[guildId]
    local subjectTpl = (gCfg and gCfg.subject ~= "") and gCfg.subject or "Welcome to {guild_name}!"
    local bodyTpl = (gCfg and gCfg.message ~= "") and gCfg.message or "Welcome {name} to {guild_name}!"

    local queue = {}
    for lowerName, dispName in pairs(pending) do
        table.insert(queue, { lower = lowerName, display = dispName })
    end

    if #queue == 0 then return end

    if not SCENE_MANAGER:IsShowing("mailSend") then
        SCENE_MANAGER:Show("mailSend")
    end

    local mailDelay = aw.mailDelay or 2500
    local total = #queue
    local sentCount = 0

    self.PrintChat(string.format("✉ |c00FFCCAutoWelcome:|r Dispatching welcome letters to %d recruit(s) with %dms pacing...", total, mailDelay))

    local function SendNext(idx)
        if idx > total then
            self.PrintChat(string.format("✓ |c59E08AAutoWelcome Complete:|r Dispatched %d welcome letter(s).", sentCount))
            if self.autoWelcomeStatusLbl then
                self.autoWelcomeStatusLbl:SetText(string.format("Dispatched %d / %d", sentCount, total))
            end
            self:RefreshAutoWelcomeDrawer()
            self:UpdateAutoWelcomeButtonBadge()
            return
        end

        local item = queue[idx]
        local subject = FR:InterpolateWelcomeLetter(subjectTpl, guildId, item.display)
        local body = FR:InterpolateWelcomeLetter(bodyTpl, guildId, item.display)

        if self.autoWelcomeStatusLbl then
            self.autoWelcomeStatusLbl:SetText(string.format("Sending (%d/%d): %s...", idx, total, item.display))
        end

        pcall(function()
            SendMail(item.display, subject, body)
        end)

        aw.welcomed[guildId] = aw.welcomed[guildId] or {}
        aw.welcomed[guildId][item.lower] = true
        aw.pending[guildId][item.lower] = nil
        sentCount = sentCount + 1

        zo_callLater(function()
            SendNext(idx + 1)
        end, mailDelay)
    end

    SendNext(1)
end

function FR:MarkAllCurrentWelcomed(guildId)
    if not self.savedVars or not self.savedVars.autoWelcome then return end
    local aw = self.savedVars.autoWelcome
    aw.welcomed[guildId] = aw.welcomed[guildId] or {}
    aw.pending[guildId] = aw.pending[guildId] or {}

    local count = 0
    for lower, _ in pairs(aw.pending[guildId]) do
        aw.welcomed[guildId][lower] = true
        count = count + 1
    end
    aw.pending[guildId] = {}

    local numM = GetNumGuildMembers(guildId)
    for m = 1, numM do
        local displayName = select(1, GetGuildMemberInfo(guildId, m))
        if displayName then
            aw.welcomed[guildId][string.lower(displayName)] = true
        end
    end

    self.PrintChat(string.format("|c59E08AAutoWelcome:|r Marked all %d active and pending members of %s as welcomed.", numM, GetGuildName(guildId)))
    self:RefreshAutoWelcomeDrawer()
    self:UpdateAutoWelcomeButtonBadge()
end

function FR:ScanRosterForNewRecruits()
    if not self.savedVars or not self.savedVars.autoWelcome then return {} end
    local aw = self.savedVars.autoWelcome
    if not aw.initialized then
        self:CaptureInitialWelcomeRoster()
        return {}
    end

    local found = {}
    for i = 1, GetNumGuilds() do
        local guildId = GetGuildId(i)
        aw.welcomed[guildId] = aw.welcomed[guildId] or {}
        aw.pending[guildId] = aw.pending[guildId] or {}
        local gCfg = aw.guildSettings and aw.guildSettings[guildId]
        local isEnabled = gCfg and gCfg.enabled

        if isEnabled then
            local numM = GetNumGuildMembers(guildId)
            for m = 1, numM do
                local displayName = select(1, GetGuildMemberInfo(guildId, m))
                if displayName then
                    local lower = string.lower(displayName)
                    if not aw.welcomed[guildId][lower] and not aw.pending[guildId][lower] then
                        aw.pending[guildId][lower] = displayName
                        table.insert(found, { guildId = guildId, displayName = displayName })
                    end
                end
            end
        end
    end

    self:UpdateAutoWelcomeButtonBadge()
    return found
end

function FR:CaptureInitialWelcomeRoster()
    if not self.savedVars or not self.savedVars.autoWelcome then return end
    local aw = self.savedVars.autoWelcome
    aw.welcomed = aw.welcomed or {}
    aw.pending = aw.pending or {}

    for i = 1, GetNumGuilds() do
        local guildId = GetGuildId(i)
        aw.welcomed[guildId] = aw.welcomed[guildId] or {}
        aw.pending[guildId] = aw.pending[guildId] or {}
        local numM = GetNumGuildMembers(guildId)
        for m = 1, numM do
            local displayName = select(1, GetGuildMemberInfo(guildId, m))
            if displayName then
                aw.welcomed[guildId][string.lower(displayName)] = true
            end
        end
    end
    aw.initialized = true
    self:UpdateAutoWelcomeButtonBadge()
end

function FR:OnGuildMemberAdded(guildId, displayName)
    if not self.savedVars or not self.savedVars.autoWelcome then return end
    local aw = self.savedVars.autoWelcome
    aw.welcomed = aw.welcomed or {}
    aw.pending = aw.pending or {}
    aw.welcomed[guildId] = aw.welcomed[guildId] or {}
    aw.pending[guildId] = aw.pending[guildId] or {}

    local lower = string.lower(displayName)
    if not aw.welcomed[guildId][lower] then
        aw.pending[guildId][lower] = displayName
        self.PrintChat(string.format("✉ |c00FFCCAutoWelcome:|r New recruit queued for %s: %s", GetGuildName(guildId), displayName))
        self:UpdateAutoWelcomeButtonBadge()
        if self.autoWelcomeDrawer and not self.autoWelcomeDrawer:IsHidden() then
            self:RefreshAutoWelcomeDrawer()
        end

        if aw.autoSendWhenMailOpen and SCENE_MANAGER:IsShowing("mailSend") then
            self:DispatchWelcomeMails(guildId)
        end
    end
end

-- Event & Hook registrations
EVENT_MANAGER:RegisterForEvent("FissalRelay_AutoWelcome_MemberAdded", EVENT_GUILD_MEMBER_ADDED, function(_, guildId, displayName)
    FR:OnGuildMemberAdded(guildId, displayName)
end)

EVENT_MANAGER:RegisterForEvent("FissalRelay_AutoWelcome_PlayerActivated", EVENT_PLAYER_ACTIVATED, function()
    local recruits = FR:ScanRosterForNewRecruits()
    if recruits and #recruits > 0 then
        FR.PrintChat(string.format("✉ |c00FFCCAutoWelcome:|r Detected %d new recruit(s) joined while offline.", #recruits))
    end
end)

if SCENE_MANAGER and SCENE_MANAGER:GetScene("mailSend") then
    SCENE_MANAGER:GetScene("mailSend"):RegisterCallback("StateChange", function(oldState, newState)
        if (newState == SCENE_SHOWING or newState == SCENE_SHOWN) and FR.savedVars and FR.savedVars.autoWelcome and FR.savedVars.autoWelcome.autoSendWhenMailOpen then
            local gIdx = FR.selectedGuildIndex or 1
            local gId = FR:ResolveGuildId(gIdx)
            FR:DispatchWelcomeMails(gId)
        end
    end)
end
