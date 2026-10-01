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
local SALES_STEPS = { 0, 1, 5000, 10000, 25000, 50000, 100000, 250000, 500000, 1000000 }
local DUES_STEPS = { 0, 500, 1000, 2500, 5000, 10000, 20000, 25000, 50000, 100000 }
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
                minSales = minSales,
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
                    minSales = 0,
                    minDues = 0,
                    mode = "OR",
                }
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

    -- Identify base (lowest) member rank
    local baseRankIndex = numRanks
    for r = numRanks, 1, -1 do
        local rCfg = gConfig.ranks[r]
        if rCfg and not rCfg.isOfficer and not self:IsOfficerCapableRank(guildId, r) then
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
                if rCfg and not rCfg.isOfficer and not self:IsOfficerCapableRank(guildId, r) then
                    local minS = rCfg.minSales or 0
                    local minD = rCfg.minDues or 0
                    local mode = rCfg.mode or "OR"
                    local met = false

                    if minS == 0 and minD == 0 then
                        met = true
                    elseif mode == "SUM" then
                        met = (salesGold + depGold) >= math.max(minS, minD)
                    elseif mode == "AND" then
                        met = (salesGold >= minS) and (depGold >= minD)
                    else -- "OR"
                        local metS = (minS == 1) and (salesCount >= 1) or (salesGold >= minS)
                        local metD = (depGold >= minD)
                        met = metS or metD
                    end

                    if met then
                        qualifiedRankIdx = r
                        if minS > 0 or minD > 0 then
                            qualifiedReason = string.format("Qualified %s (Sales: %sg, Dues: %sg)",
                                rCfg.name, ZO_LocalizeDecimalNumber(salesGold), ZO_LocalizeDecimalNumber(depGold))
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
    title:SetFont("ZoFontGameBold")
    title:SetText("|cFF9900AUTO-RANK LEADERBOARD|r  |c00FFCC(Dues & Performance Automation)|r")

    local statSummaryLbl = wm:CreateControl("$(parent)_Stats", card, CT_LABEL)
    statSummaryLbl:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 8)
    statSummaryLbl:SetFont("ZoFontGameSmall")
    statSummaryLbl:SetText("Evaluated: --  |  ▲ Promote: --  |  ▼ Demote: --  |  ● Kept: --")
    self.autoRanksSummaryLbl = statSummaryLbl

    -- 3. Filter Bar & Action Buttons
    local controlRow = wm:CreateControl("$(parent)_Controls", card, CT_CONTROL)
    controlRow:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 32)
    controlRow:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 32)
    controlRow:SetHeight(28)

    -- Filter Buttons: All, Changes Only, Promotes, Demotes, Exempt
    local filters = {
        { id = "all", label = "All", width = 60 },
        { id = "changes", label = "Changes", width = 75 },
        { id = "promote", label = "▲ Promote", width = 85 },
        { id = "demote", label = "▼ Demote", width = 85 },
        { id = "exempt", label = "🛡 Exempt", width = 80 },
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
        curX = curX + f.width + 6
    end

    -- Lookback Window cycle button
    local windowBtn = wm:CreateControl("$(parent)_WindowBtn", controlRow, CT_BUTTON)
    windowBtn:SetAnchor(TOPLEFT, controlRow, TOPLEFT, curX + 4, 2)
    windowBtn:SetDimensions(76, 24)
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

    -- [⚙ Configure Rank Dues] Button
    local configBtn = wm:CreateControl("$(parent)_ConfigBtn", controlRow, CT_BUTTON)
    configBtn:SetAnchor(TOPLEFT, controlRow, TOPLEFT, curX + 86, 2)
    configBtn:SetDimensions(135, 24)
    configBtn:SetFont("ZoFontGameBold")
    configBtn:SetText("⚙ Rank Dues Setup")
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

    -- Right Action Buttons: Apply Changes, Evaluate, Abort
    local applyBtn = wm:CreateControl("$(parent)_ApplyBtn", controlRow, CT_BUTTON)
    applyBtn:SetAnchor(TOPRIGHT, controlRow, TOPRIGHT, 0, 2)
    applyBtn:SetDimensions(140, 24)
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
    abortBtn:SetDimensions(140, 24)
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
    evalBtn:SetAnchor(RIGHT, applyBtn, LEFT, -8, 0)
    evalBtn:SetDimensions(80, 24)
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

    local progLbl = wm:CreateControl("$(parent)_ProgLbl", controlRow, CT_LABEL)
    progLbl:SetAnchor(RIGHT, evalBtn, LEFT, -10, 0)
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

    -- Master Checkbox
    local masterCheck = wm:CreateControl("$(parent)_MasterCheck", headerRow, CT_BUTTON)
    masterCheck:SetAnchor(LEFT, headerRow, LEFT, 8, 0)
    masterCheck:SetDimensions(20, 20)
    masterCheck:SetFont("ZoFontGameBold")
    masterCheck:SetText("[✓]")
    masterCheck:SetNormalFontColor(0, 1, 0.8, 1)
    masterCheck.allSelected = true
    masterCheck:SetHandler("OnClicked", function()
        masterCheck.allSelected = not masterCheck.allSelected
        masterCheck:SetText(masterCheck.allSelected and "[✓]" or "[  ]")
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
        checkBtn:SetText("[✓]")

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
end

--[[ =========================================================================
     RANK DUES CONFIGURATION DRAWER MODAL
========================================================================= ]]--

function FR:BuildRankConfigDrawer(parent)
    local wm = WINDOW_MANAGER
    local drawer = wm:CreateControl("$(parent)_RankDrawer", parent, CT_BACKDROP)
    drawer:SetAnchor(TOPLEFT, parent, TOPLEFT, 15, 60)
    drawer:SetAnchor(BOTTOMRIGHT, parent, BOTTOMRIGHT, -15, -35)
    drawer:SetCenterColor(0.04, 0.04, 0.06, 0.98)
    drawer:SetEdgeColor(0.95, 0.70, 0.20, 0.95)
    drawer:SetEdgeTexture("", 8, 1, 0)
    drawer:SetHidden(true)
    self.rankConfigDrawer = drawer

    -- Drawer Header
    local dTitle = wm:CreateControl("$(parent)_Title", drawer, CT_LABEL)
    dTitle:SetAnchor(TOPLEFT, drawer, TOPLEFT, 16, 12)
    dTitle:SetFont("ZoFontGameBold")
    dTitle:SetText("|cFF9900RANK DUES & LEADERBOARD THRESHOLDS|r  |c00FFCC(Setup Ladder)|r")

    local closeBtn = wm:CreateControl("$(parent)_CloseBtn", drawer, CT_BUTTON)
    closeBtn:SetAnchor(TOPRIGHT, drawer, TOPRIGHT, -14, 10)
    closeBtn:SetDimensions(26, 26)
    closeBtn:SetFont("ZoFontGameBold")
    closeBtn:SetText("|cFF5555✕|r")
    closeBtn:SetHandler("OnClicked", function()
        drawer:SetHidden(true)
    end)

    local dSub = wm:CreateControl("$(parent)_Sub", drawer, CT_LABEL)
    dSub:SetAnchor(TOPLEFT, drawer, TOPLEFT, 16, 36)
    dSub:SetFont("ZoFontGameSmall")
    dSub:SetText("Configure minimum sales gold and bank dues per rank. Ladder evaluates from Rank 3 down to lowest rank.")

    -- Rank Configuration Rows (Up to 8 ranks)
    self.rankDrawerRows = {}
    local rowY = 62

    for r = 1, 8 do
        local rCtrl = wm:CreateControl("$(parent)_RRow_" .. r, drawer, CT_BACKDROP)
        rCtrl:SetAnchor(TOPLEFT, drawer, TOPLEFT, 16, rowY + (r - 1) * 34)
        rCtrl:SetAnchor(TOPRIGHT, drawer, TOPRIGHT, -16, rowY + (r - 1) * 34)
        rCtrl:SetHeight(30)
        rCtrl:SetCenterColor(0.06, 0.06, 0.09, 0.70)
        rCtrl:SetEdgeColor(0.20, 0.20, 0.25, 0.50)
        rCtrl:SetEdgeTexture("", 8, 1, 0)

        local rNameLbl = wm:CreateControl("$(parent)_Name", rCtrl, CT_LABEL)
        rNameLbl:SetAnchor(LEFT, rCtrl, LEFT, 10, 0)
        rNameLbl:SetDimensions(180, 22)
        rNameLbl:SetFont("ZoFontGameBold")
        rNameLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        rCtrl.nameLbl = rNameLbl

        -- Sales Threshold Stepper
        local sDecBtn = wm:CreateControl("$(parent)_SDec", rCtrl, CT_BUTTON)
        sDecBtn:SetAnchor(LEFT, rNameLbl, RIGHT, 8, 0)
        sDecBtn:SetDimensions(22, 22)
        sDecBtn:SetFont("ZoFontGameBold")
        sDecBtn:SetText("◀")
        self:StyleTactileButton(sDecBtn, { normalBg = { 0.1, 0.1, 0.15, 0.8 } })

        local sValLbl = wm:CreateControl("$(parent)_SVal", rCtrl, CT_LABEL)
        sValLbl:SetAnchor(LEFT, sDecBtn, RIGHT, 4, 0)
        sValLbl:SetDimensions(110, 22)
        sValLbl:SetFont("ZoFontGameSmall")
        sValLbl:SetHorizontalAlignment(TEXT_ALIGN_CENTER)
        sValLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        rCtrl.salesLbl = sValLbl

        local sIncBtn = wm:CreateControl("$(parent)_SInc", rCtrl, CT_BUTTON)
        sIncBtn:SetAnchor(LEFT, sValLbl, RIGHT, 4, 0)
        sIncBtn:SetDimensions(22, 22)
        sIncBtn:SetFont("ZoFontGameBold")
        sIncBtn:SetText("▶")
        self:StyleTactileButton(sIncBtn, { normalBg = { 0.1, 0.1, 0.15, 0.8 } })

        -- Dues Threshold Stepper
        local dDecBtn = wm:CreateControl("$(parent)_DDec", rCtrl, CT_BUTTON)
        dDecBtn:SetAnchor(LEFT, sIncBtn, RIGHT, 16, 0)
        dDecBtn:SetDimensions(22, 22)
        dDecBtn:SetFont("ZoFontGameBold")
        dDecBtn:SetText("◀")
        self:StyleTactileButton(dDecBtn, { normalBg = { 0.1, 0.1, 0.15, 0.8 } })

        local dValLbl = wm:CreateControl("$(parent)_DVal", rCtrl, CT_LABEL)
        dValLbl:SetAnchor(LEFT, dDecBtn, RIGHT, 4, 0)
        dValLbl:SetDimensions(100, 22)
        dValLbl:SetFont("ZoFontGameSmall")
        dValLbl:SetHorizontalAlignment(TEXT_ALIGN_CENTER)
        dValLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        rCtrl.duesLbl = dValLbl

        local dIncBtn = wm:CreateControl("$(parent)_DInc", rCtrl, CT_BUTTON)
        dIncBtn:SetAnchor(LEFT, dValLbl, RIGHT, 4, 0)
        dIncBtn:SetDimensions(22, 22)
        dIncBtn:SetFont("ZoFontGameBold")
        dIncBtn:SetText("▶")
        self:StyleTactileButton(dIncBtn, { normalBg = { 0.1, 0.1, 0.15, 0.8 } })

        -- Mode Stepper (OR, SUM, AND)
        local modeBtn = wm:CreateControl("$(parent)_ModeBtn", rCtrl, CT_BUTTON)
        modeBtn:SetAnchor(LEFT, dIncBtn, RIGHT, 16, 0)
        modeBtn:SetDimensions(90, 22)
        modeBtn:SetFont("ZoFontGameSmall")
        modeBtn:SetText("Mode: OR")
        self:StyleTactileButton(modeBtn, {
            normalBg = { 0.08, 0.12, 0.10, 0.85 },
            hoverBg = { 0.12, 0.18, 0.15, 0.95 },
            normalEdge = { 0.20, 0.60, 0.40, 0.70 },
        })
        rCtrl.modeBtn = modeBtn

        -- Officer / Immune Status Label
        local offLbl = wm:CreateControl("$(parent)_OffLbl", rCtrl, CT_LABEL)
        offLbl:SetAnchor(RIGHT, rCtrl, RIGHT, -10, 0)
        offLbl:SetFont("ZoFontGameSmall")
        offLbl:SetText("")
        rCtrl.offLbl = offLbl

        rCtrl.sDecBtn = sDecBtn
        rCtrl.sIncBtn = sIncBtn
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
        drawer:SetHidden(true)
        local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
        self:EvaluateAutoRanks(gId)
        self:UpdateAutoRanksUI()
        self.PrintChat("Rank ladder thresholds saved! Roster re-evaluated.")
    end)
end

function FR:ToggleRankConfigDrawer()
    if not self.rankConfigDrawer then return end
    local show = self.rankConfigDrawer:IsHidden()
    self.rankConfigDrawer:SetHidden(not show)
    if show then
        self:RefreshRankConfigDrawer()
    end
end

function FR:RefreshRankConfigDrawer()
    local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
    local gConfig = self:GetGuildRankConfig(gId)
    local numRanks = GetNumGuildRanks(gId)

    for r = 1, #self.rankDrawerRows do
        local rCtrl = self.rankDrawerRows[r]
        if r <= numRanks then
            rCtrl:SetHidden(false)
            local rCfg = gConfig.ranks[r] or { name = GetFinalGuildRankName(gId, r), minSales = 0, minDues = 0, mode = "OR", isOfficer = (r <= 2) }
            local isOfficer = rCfg.isOfficer or self:IsOfficerCapableRank(gId, r) or (r <= 2)

            rCtrl.nameLbl:SetText(string.format("|cFFFFFF#%d: %s|r", r, rCfg.name))

            if isOfficer then
                rCtrl.salesLbl:SetText("|c888888[Immune]|r")
                rCtrl.duesLbl:SetText("|c888888[Immune]|r")
                rCtrl.modeBtn:SetHidden(true)
                rCtrl.sDecBtn:SetHidden(true)
                rCtrl.sIncBtn:SetHidden(true)
                rCtrl.dDecBtn:SetHidden(true)
                rCtrl.dIncBtn:SetHidden(true)
                rCtrl.offLbl:SetText("|c59E08A🛡 Officer Immunity|r")
            else
                rCtrl.modeBtn:SetHidden(false)
                rCtrl.sDecBtn:SetHidden(false)
                rCtrl.sIncBtn:SetHidden(false)
                rCtrl.dDecBtn:SetHidden(false)
                rCtrl.dIncBtn:SetHidden(false)
                rCtrl.offLbl:SetText("")

                -- Format Sales label
                local sText = (rCfg.minSales == 1 and "1 Sale")
                    or (rCfg.minSales == 0 and "No Sales Min")
                    or string.format("Sales: %sg", ZO_LocalizeDecimalNumber(rCfg.minSales))
                rCtrl.salesLbl:SetText(string.format("|cFFD700%s|r", sText))

                -- Format Dues label
                local dText = (rCfg.minDues == 0 and "No Dues Min")
                    or string.format("Dues: %sg", ZO_LocalizeDecimalNumber(rCfg.minDues))
                rCtrl.duesLbl:SetText(string.format("|c59E08A%s|r", dText))

                -- Format Mode
                rCtrl.modeBtn:SetText("Mode: " .. (rCfg.mode or "OR"))

                -- Hook Stepper Handlers
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
                    self:RefreshRankConfigDrawer()
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
                    self:RefreshRankConfigDrawer()
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
                    self:RefreshRankConfigDrawer()
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
                    self:RefreshRankConfigDrawer()
                end)

                rCtrl.modeBtn:SetHandler("OnClicked", function()
                    local curM = rCfg.mode or "OR"
                    local nextM = (curM == "OR" and "SUM") or (curM == "SUM" and "AND") or "OR"
                    rCfg.mode = nextM
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
        self.autoRanksSummaryLbl:SetText(string.format("Evaluated: |cFFFFFF%d|r  |  |c59E08A▲ Promote: %d|r  |  |cFF6666▼ Demote: %d|r  |  |c888888● Kept: %d|r  |  |cE6C387🛡 Exempt: %d|r",
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
                    rCtrl.check:SetText(item.selected and "|c00FFCC[✓]|r" or "|c555555[  ]|r")
                    rCtrl.check:SetHandler("OnClicked", function()
                        item.selected = not item.selected
                        self:RefreshAutoRanksGrid()
                    end)
                end

                rCtrl.member:SetText(string.format("|cFFFFFF%s|r", item.displayName))
                rCtrl.curRank:SetText(string.format("|cCCCCCC%s|r", item.currentRankName))

                local tgtColor = (item.action == "PROMOTE" and "|c59E08A▲ ")
                    or (item.action == "DEMOTE" and "|cFF6666▼ ")
                    or "|c888888● "
                rCtrl.tgtRank:SetText(string.format("%s%s|r", tgtColor, item.targetRankName))

                local actBadge = "|c888888● KEEP|r"
                if item.isExempt then
                    actBadge = "|cE6C387🛡 EXEMPT|r"
                elseif item.action == "PROMOTE" then
                    actBadge = "|c59E08A▲ PROMOTE|r"
                elseif item.action == "DEMOTE" then
                    actBadge = "|cFF6666▼ DEMOTE|r"
                end
                rCtrl.action:SetText(actBadge)

                rCtrl.sales:SetText(item.salesGold > 0 and string.format("|cFFD700%sg|r", ZO_LocalizeDecimalNumber(item.salesGold)) or "|c555555--|r")
                rCtrl.dep:SetText(item.donations > 0 and string.format("|c59E08A%sg|r", ZO_LocalizeDecimalNumber(item.donations)) or "|c555555--|r")
                rCtrl.note:SetText(string.format("|c999999%s|r", item.status or ""))
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
    local gId = self:ResolveGuildId(self.selectedGuildIndex or 1)
    if not self.autoRankResults or #self.autoRankResults == 0 then
        self:EvaluateAutoRanks(gId)
    end
    if self.autoRanksWindowBtn then
        self.autoRanksWindowBtn:SetText((self.autoRankLookbackDays or 10) .. " Days")
    end
    self:UpdateAutoRanksFilterButtons()
    self:RefreshAutoRanksGrid()
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
