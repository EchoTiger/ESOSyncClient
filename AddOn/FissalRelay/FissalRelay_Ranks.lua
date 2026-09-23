--[[
    FissalRelay_Ranks.lua
    Auto-Ranker & Roster Automation Engine for Fissal's Cogwork Relay
    Crafted by Echo & Fissal for Fissal Relay and the Redfur Guilds.

    Features:
      • Automatic member promotion & demotion based on sales and bank donations
      • In-depth evaluation against grounded Redfur Dues rules (10d/14d/15d windows)
      • High-safety guards:
          - Officer Immunity (Ranks 1 & 2 never touched)
          - DNR Note Shield (skips members whose note contains 'DNR')
          - New Member Probation (protects recruits within 7-day grace period)
          - Demotion Step Cap (limits drops to 1 rank tier per evaluation)
          - Restrict Demotions mode (optional promotion-only run)
      • Interactive Console Tab 6 UI with visual preview table
      • Per-row checkbox inclusion/exclusion before applying changes
      • Paced batch executor (750ms spacing) using GuildSetRank with emergency abort
      • Full slash command routing (/fissal ranks, /fr ranks, /ar)
]]--

FissalRelay = FissalRelay or {}
local FR = FissalRelay

FR.autoRankResults = {}
FR.autoRankTasks = {}
FR.autoRankBatchRunning = false
FR.autoRankFilter = "all"
FR.autoRankLookbackDays = 10

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
        }
    end
    local ar = self.savedVars.autoRanks
    if ar.officerImmunity == nil then ar.officerImmunity = true end
    if ar.protectDNR == nil then ar.protectDNR = true end
    if ar.probationDays == nil then ar.probationDays = 7 end
    if ar.demoteCap == nil then ar.demoteCap = 1 end
    if ar.restrictDemotions == nil then ar.restrictDemotions = false end
    if ar.lookbackDays == nil then ar.lookbackDays = 10 end
    self.autoRankLookbackDays = ar.lookbackDays
end

--[[ =========================================================================
     DYNAMIC RANK RESOLUTION & SCHEMA FINGERPRINTING (Fable 5.1 Architecture)
========================================================================= ]]--

function FR:IsOfficerCapableRank(guildId, rankIndex)
    if not guildId or not rankIndex then return false end
    if IsGuildRankGuildMaster and IsGuildRankGuildMaster(guildId, rankIndex) then return true end
    if DoesGuildRankHavePermission then
        for _, perm in pairs({ GUILD_PERMISSION_PROMOTE, GUILD_PERMISSION_DEMOTE,
                               GUILD_PERMISSION_REMOVE, GUILD_PERMISSION_INVITE,
                               GUILD_PERMISSION_NOTE_EDIT, GUILD_PERMISSION_SET_MOTD }) do
            if perm and DoesGuildRankHavePermission(guildId, rankIndex, perm) then return true end
        end
    end
    return rankIndex <= 2  -- conservative floor
end

function FR:ResolveRankIndex(guildId, wantedName)
    if not guildId or guildId == 0 or not wantedName then return nil end
    local numRanks = GetNumGuildRanks(guildId)
    local target = string.lower(wantedName)
    local names = {}
    for r = 1, numRanks do names[r] = string.lower(GetFinalGuildRankName(guildId, r) or "") end

    -- Pass 1: exact match
    for r = 1, numRanks do
        if names[r] == target then return r end
    end
    -- Pass 2: substring, bottom-up, non-officer only (Fable 5.1 B2)
    for r = numRanks, 1, -1 do
        if string.find(names[r], target, 1, true) and not self:IsOfficerCapableRank(guildId, r) then
            return r
        end
    end
    return nil
end

--[[ =========================================================================
     EVALUATION ENGINE
========================================================================= ]]--

function FR:EvaluateAutoRanks(guildId)
    self:EnsureAutoRankState()
    guildId = self:ResolveGuildId(guildId or self.selectedGuildIndex or 1)
    if not guildId or guildId == 0 then return {} end

    local guildName = GetGuildName(guildId)
    local numMembers = GetNumGuildMembers(guildId)
    local numRanks = GetNumGuildRanks(guildId)
    local rule = self:GetRedfurDuesRule(guildId)
    local lookbackDays = self.autoRankLookbackDays or rule.windowDays or 10
    local arSettings = self.savedVars.autoRanks

    -- Permission Pre-flight: verify Note Read authority (Fable 5.1 S6)
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

    -- Identify guild rank indices dynamically by name (Fable 5.1 Architecture)
    local windowShopperRankIdx = self:ResolveRankIndex(guildId, "window shopper") or self:ResolveRankIndex(guildId, "shopper") or numRanks
    local defaultTraderRankIdx = self:ResolveRankIndex(guildId, "trader") or math.max(numRanks - 2, 3)

    -- Critical B2 & P3 Assertions (Fable 5.1): Target ranks must be in range and NEVER officer-capable
    if defaultTraderRankIdx > numRanks or windowShopperRankIdx > numRanks then
        self.PrintChat(string.format("|cFF5555[Configuration Error]|r Target rank index exceeds guild rank count (%d)! Auto-Rank aborted.", numRanks))
        return {}
    end
    if self:IsOfficerCapableRank(guildId, defaultTraderRankIdx) or self:IsOfficerCapableRank(guildId, windowShopperRankIdx) then
        self.PrintChat(string.format("|cFF5555[Security Block]|r Target rank (%s / %s) possesses officer permissions! Auto-Rank aborted.",
            GetFinalGuildRankName(guildId, defaultTraderRankIdx), GetFinalGuildRankName(guildId, windowShopperRankIdx)))
        return {}
    end

    local myAccount = string.lower(string.gsub(GetDisplayName() or "", "^@", ""))
    local myMemberIdx = GetGuildMemberIndexFromDisplayName and GetGuildMemberIndexFromDisplayName(guildId, GetDisplayName())
    local myRankIndex = 99
    if myMemberIdx and myMemberIdx > 0 then
        local _, _, mRank = GetGuildMemberInfo(guildId, myMemberIdx)
        if mRank then myRankIndex = mRank end
    end

    local results = {}
    local nowTs = GetTimeStamp()

    for i = 1, numMembers do
        local displayName, note, rankIndex, playerStatus, secsSinceLogoff = GetGuildMemberInfo(guildId, i)
        local rawName = string.gsub(displayName, "^@", ""):lower()
        local currentRankName = GetFinalGuildRankName(guildId, rankIndex)
        local cleanNote = note or ""
        local noteLower = string.lower(cleanNote)

        local sData = salesByMember[rawName] or { count = 0, gold = 0 }
        local salesGold = sData.gold or 0
        local salesCount = sData.count or 0
        local depGold = bankDeposits[rawName] or 0

        local duesMet, reason = self:EvaluateRedfurDues(guildId, rawName, depGold, salesCount, salesGold)

        local action = "KEEP"
        local targetRankIndex = rankIndex
        local statusReason = "Dues Met"

        -- Officer & Authority Checks (Fable 5.1 Permission-Based Immunity)
        local isSelf = (rawName == myAccount)
        local isOfficerRank = self:IsOfficerCapableRank(guildId, rankIndex)
        local isAtOrAboveMyRank = (rankIndex <= myRankIndex)

        -- Frontier-pattern DNR shield matching whole word 'dnr' on markup-stripped note (Fable 5.1 S5)
        local plainNote = string.gsub(string.gsub(noteLower, "|c%x%x%x%x%x%x", ""), "|r", "")
        local isDNR = string.find(plainNote, "%f[%w]dnr%f[%W]") ~= nil

        -- Safety Guard 0: Self Protection & Rank Authority Shield
        if isSelf then
            statusReason = "Self Protection"
            action = "KEEP"
        elseif isAtOrAboveMyRank then
            statusReason = "At/Above My Rank"
            action = "KEEP"

        -- Safety Guard 1: Permission-Based Officer Immunity
        elseif arSettings.officerImmunity and isOfficerRank then
            statusReason = "Officer Immunity"
            action = "KEEP"

        -- Safety Guard 2: Frontier DNR Note Shield
        elseif arSettings.protectDNR and isDNR then
            statusReason = "DNR Shield"
            action = "KEEP"

        -- Safety Guard 3: New Member Probation
        elseif arSettings.probationDays > 0 and secsSinceLogoff < (arSettings.probationDays * 86400) and rankIndex == numRanks then
            statusReason = "Probation Grace"
            action = "KEEP"

        else
            -- Evaluate rank transition based on guild rules
            if rule.ruleType == "DEALERS" then
                -- 25,000g sales + deposit requirement
                if not duesMet then
                    -- Failed dues: Target is Window Shopper
                    if rankIndex ~= windowShopperRankIdx then
                        targetRankIndex = windowShopperRankIdx
                        action = "DEMOTE"
                        statusReason = string.format("Missing Dues (%sg / 25k)", ZO_CommaDelimitNumber(salesGold + depGold))
                    else
                        targetRankIndex = windowShopperRankIdx
                        action = "KEEP"
                        statusReason = "On Window Shopper"
                    end
                else
                    -- Met dues: If on Window Shopper, promote back to Trader!
                    if rankIndex == windowShopperRankIdx then
                        targetRankIndex = defaultTraderRankIdx
                        action = "PROMOTE"
                        statusReason = string.format("Dues Met (%sg)", ZO_CommaDelimitNumber(salesGold + depGold))
                    else
                        -- Member is in good standing
                        action = "KEEP"
                        statusReason = "Dues Met"
                    end
                end

            elseif rule.ruleType == "POST" then
                -- 1 sale or 1,000g deposit in 10 days
                if not duesMet then
                    if rankIndex < numRanks then
                        targetRankIndex = numRanks
                        action = "DEMOTE"
                        statusReason = "0 Sales / 0 Deposits"
                    else
                        action = "KEEP"
                        statusReason = "Lowest Rank"
                    end
                else
                    if rankIndex == numRanks then
                        targetRankIndex = defaultTraderRankIdx
                        action = "PROMOTE"
                        statusReason = string.format("Dues Met (%s sales, %sg)", salesCount, ZO_CommaDelimitNumber(depGold))
                    else
                        action = "KEEP"
                        statusReason = "Dues Met"
                    end
                end

            elseif rule.ruleType == "CARAVAN" then
                -- >= 1 sale in 15 days
                if not duesMet then
                    if rankIndex < numRanks then
                        targetRankIndex = numRanks
                        action = "DEMOTE"
                        statusReason = "0 Sales in 15d"
                    else
                        action = "KEEP"
                        statusReason = "Lowest Rank"
                    end
                else
                    if rankIndex == numRanks then
                        targetRankIndex = defaultTraderRankIdx
                        action = "PROMOTE"
                        statusReason = string.format("%d Sales", salesCount)
                    else
                        action = "KEEP"
                        statusReason = "Active Seller"
                    end
                end
            else
                action = "KEEP"
                statusReason = "Standard"
            end

            -- Apply Restrict Demotions safety option
            if arSettings.restrictDemotions and action == "DEMOTE" then
                action = "KEEP"
                targetRankIndex = rankIndex
                statusReason = "Demotion Restricted"
            end

            -- Apply Demote Step Cap (e.g. max 1 tier drop)
            if action == "DEMOTE" and arSettings.demoteCap and arSettings.demoteCap > 0 then
                local maxDrop = rankIndex + arSettings.demoteCap
                if targetRankIndex > maxDrop then
                    targetRankIndex = maxDrop
                end
                if targetRankIndex == rankIndex then
                    action = "KEEP"
                end
            end
        end

        local targetRankName = GetFinalGuildRankName(guildId, targetRankIndex)

        table.insert(results, {
            memberIndex = i,
            displayName = displayName,
            note = cleanNote,
            currentRankIndex = rankIndex,
            currentRankName = currentRankName,
            targetRankIndex = targetRankIndex,
            targetRankName = targetRankName,
            action = action,
            salesGold = salesGold,
            salesCount = salesCount,
            donations = depGold,
            duesMet = duesMet,
            status = statusReason,
            selected = (action == "PROMOTE" or action == "DEMOTE"),
        })
    end

    -- Sort: Promotions first, then Demotions, then Keep
    table.sort(results, function(a, b)
        local order = { PROMOTE = 1, DEMOTE = 2, KEEP = 3 }
        local oa = order[a.action] or 4
        local ob = order[b.action] or 4
        if oa ~= ob then return oa < ob end
        if a.action == "DEMOTE" and b.action == "DEMOTE" then
            return a.currentRankIndex < b.currentRankIndex
        end
        return a.displayName < b.displayName
    end)

    self.autoRankResults = results
    return results
end

--[[ =========================================================================
     ACK-GATED BATCH EXECUTION ENGINE (Fable 5.1 Inquiry 1)
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
    -- Re-entrancy hygiene: ensure previous state is cleanly flushed (Fable 5.1 S7)
    if self.autoRankBatchRunning then
        self.PrintChat("|cFF5555[Fissal Ranks]|r Batch is already currently running!")
        return
    end
    self:CleanupAutoRankBatch()

    guildId = self:ResolveGuildId(guildId or self.selectedGuildIndex or 1)
    local guildName = GetGuildName(guildId)

    local hasPromote = DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_PROMOTE)
    local hasDemote = DoesPlayerHaveGuildPermission(guildId, GUILD_PERMISSION_DEMOTE)
    local isGM = IsPlayerGuildMaster and IsPlayerGuildMaster(guildId)

    if not (isGM or (hasPromote and hasDemote)) then
        self.PrintChat(string.format("|cFF5555Permission Denied:|r You need both Promote and Demote permissions in %s to run auto-ranks.", guildName))
        return
    end

    local setRankFn = GuildSetRank or GuildSetMemberRank or SetGuildMemberRank
    if not setRankFn then
        self.PrintChat("|cFF5555[Error]|r Set guild rank API function not found on client! Aborting batch.")
        return
    end

    -- Collect selected tasks
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

    self.PrintChat(string.format("=== |cFF9900[Fissal Ranks]|r Starting Ack-Gated Rank Updates for %s (%d queued) ===", guildName, #tasks))
    PlaySound(SOUNDS.GUILD_ROSTER_ADDED or SOUNDS.NOTE_SAVED)

    if self.autoRanksApplyBtn then self.autoRanksApplyBtn:SetHidden(true) end
    if self.autoRanksAbortBtn then self.autoRanksAbortBtn:SetHidden(false) end

    -- Rank schema drift guards: Abort immediately if GM edits ranks mid-batch (Fable 5.1 S7)
    EVENT_MANAGER:RegisterForEvent("FissalRelay_AutoRankSchemaDrift1", EVENT_GUILD_RANKS_CHANGED, function()
        FR.PrintChat("|cFF5555[Security Abort]|r Guild rank structure changed mid-batch! Aborting for safety.")
        FR:AbortAutoRankBatch()
    end)
    EVENT_MANAGER:RegisterForEvent("FissalRelay_AutoRankSchemaDrift2", EVENT_GUILD_RANK_CHANGED, function()
        FR.PrintChat("|cFF5555[Security Abort]|r Guild rank definition changed mid-batch! Aborting for safety.")
        FR:AbortAutoRankBatch()
    end)

    -- Ack listener for EVENT_GUILD_MEMBER_RANK_CHANGED (Fable 5.1 V1: Closes over guildId and validates newRank)
    EVENT_MANAGER:RegisterForEvent("FissalRelay_AutoRankAck", EVENT_GUILD_MEMBER_RANK_CHANGED, function(_, eventGuildId, displayName, newRank)
        local pending = FR.autoRankPending
        if not pending then return end

        local cleanPending = string.lower(string.gsub(pending.displayName or "", "^@", ""))
        local cleanEvent = string.lower(string.gsub(displayName or "", "^@", ""))

        if eventGuildId == guildId and cleanPending == cleanEvent and newRank == pending.targetRankIndex then
            FR.autoRankPending = nil
            FR.autoRankSucceeded = (FR.autoRankSucceeded or 0) + 1
            FR.autoRankProcessedCount = (FR.autoRankProcessedCount or 0) + 1
            FR.autoRankNextAllowedAt = GetGameTimeMilliseconds() + MIN_SPACING_MS

            local actColor = pending.action == "PROMOTE" and "59E08A" or "FF6666"
            FR.PrintChat(string.format("  • [|c%s%s ✓|r] %s: %s -> |c00FFCC%s|r",
                actColor, pending.action, pending.displayName, pending.currentRankName, pending.targetRankName))

            if FR.autoRanksProgressLbl then
                FR.autoRanksProgressLbl:SetText(string.format("Applying: %d / %d", FR.autoRankProcessedCount, FR.autoRankTotalTasks))
            end
        end
    end)

    local taskIdx = 1

    EVENT_MANAGER:RegisterForUpdate("FissalRelay_AutoRankBatch", 150, function()
        if not FR.autoRankBatchRunning then
            FR:CleanupAutoRankBatch()
            return
        end

        local now = GetGameTimeMilliseconds()

        -- 1. Check pending job timeout with late-ack re-validation (Fable 5.1 S1)
        if FR.autoRankPending then
            if (now - FR.autoRankSentAt) > ACK_TIMEOUT_MS then
                local p = FR.autoRankPending
                local mIdx = GetGuildMemberIndexFromDisplayName and GetGuildMemberIndexFromDisplayName(guildId, p.displayName)
                local _, _, liveRank = (mIdx and mIdx > 0) and GetGuildMemberInfo(guildId, mIdx)
                if liveRank == p.targetRankIndex then
                    FR.autoRankSucceeded = (FR.autoRankSucceeded or 0) + 1
                    FR.PrintChat(string.format("  • [|c59E08ALate Ack ✓|r] %s verified at target rank %s.", p.displayName, p.targetRankName))
                else
                    FR.autoRankTimedOut = (FR.autoRankTimedOut or 0) + 1
                    FR.PrintChat(string.format("  • |cFFCC00[Timeout]|r %s rank change unacked after %dms. Pacing next member...", p.displayName, ACK_TIMEOUT_MS))
                end
                FR.autoRankPending = nil
                FR.autoRankProcessedCount = (FR.autoRankProcessedCount or 0) + 1
                FR.autoRankNextAllowedAt = now + MIN_SPACING_MS

                if FR.autoRanksProgressLbl then
                    FR.autoRanksProgressLbl:SetText(string.format("Applying: %d / %d", FR.autoRankProcessedCount, FR.autoRankTotalTasks))
                end
            else
                return -- Wait for server ack
            end
        end

        -- 2. Respect spacing pacing
        if now < FR.autoRankNextAllowedAt then return end

        -- 3. Check queue completion
        if taskIdx > #FR.autoRankTasks then
            FR:CleanupAutoRankBatch()
            FR.PrintChat(string.format("✓ |c59E08A[Fissal Ranks]|r Batch Complete for %s: %d Succeeded, %d Timed Out, %d Skipped (Total: %d)",
                guildName, FR.autoRankSucceeded or 0, FR.autoRankTimedOut or 0, FR.autoRankSkipped or 0, FR.autoRankTotalTasks or 0))
            PlaySound(SOUNDS.LEVEL_UP or SOUNDS.GUILD_ROSTER_ADDED)

            -- Re-evaluate roster to show updated state
            FR:EvaluateAutoRanks(guildId)
            FR:UpdateAutoRanksUI()
            return
        end

        local item = FR.autoRankTasks[taskIdx]
        taskIdx = taskIdx + 1

        if item then
            -- Live Roster Re-validation (Fable 5.1): Member still present and rank unchanged externally?
            local memberIdx = GetGuildMemberIndexFromDisplayName and GetGuildMemberIndexFromDisplayName(guildId, item.displayName)
            if not memberIdx or memberIdx <= 0 then
                FR.PrintChat(string.format("  • |c888888[Skipped]|r %s is no longer in guild.", item.displayName))
                FR.autoRankSkipped = (FR.autoRankSkipped or 0) + 1
                FR.autoRankProcessedCount = (FR.autoRankProcessedCount or 0) + 1
                FR.autoRankNextAllowedAt = now + 100
                if FR.autoRanksProgressLbl then
                    FR.autoRanksProgressLbl:SetText(string.format("Applying: %d / %d", FR.autoRankProcessedCount, FR.autoRankTotalTasks))
                end
                return
            end

            local _, _, currentRank = GetGuildMemberInfo(guildId, memberIdx)
            if currentRank ~= item.currentRankIndex then
                FR.PrintChat(string.format("  • |c888888[Skipped]|r %s rank changed externally (expected %d, found %d).",
                    item.displayName, item.currentRankIndex, currentRank))
                FR.autoRankSkipped = (FR.autoRankSkipped or 0) + 1
                FR.autoRankProcessedCount = (FR.autoRankProcessedCount or 0) + 1
                FR.autoRankNextAllowedAt = now + 100
                if FR.autoRanksProgressLbl then
                    FR.autoRanksProgressLbl:SetText(string.format("Applying: %d / %d", FR.autoRankProcessedCount, FR.autoRankTotalTasks))
                end
                return
            end

            if currentRank == item.targetRankIndex then
                FR.PrintChat(string.format("  • |c888888[Skipped]|r %s is already at target rank.", item.displayName))
                FR.autoRankSkipped = (FR.autoRankSkipped or 0) + 1
                FR.autoRankProcessedCount = (FR.autoRankProcessedCount or 0) + 1
                FR.autoRankNextAllowedAt = now + 100
                if FR.autoRanksProgressLbl then
                    FR.autoRanksProgressLbl:SetText(string.format("Applying: %d / %d", FR.autoRankProcessedCount, FR.autoRankTotalTasks))
                end
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

    -- 2. Title & Status
    local title = wm:CreateControl("$(parent)_Title", card, CT_LABEL)
    title:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 10)
    title:SetFont("ZoFontGameBold")
    title:SetText("|cFF9900AUTO-RANK ROSTER AUTOMATION|r • |c00FFCCSmart Promotion & Dues Protection|r")

    local statSummaryLbl = wm:CreateControl("$(parent)_Stats", card, CT_LABEL)
    statSummaryLbl:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 10)
    statSummaryLbl:SetFont("ZoFontGameSmall")
    statSummaryLbl:SetText("Evaluated: --  |  Promote: --  |  Demote: --")
    self.autoRanksSummaryLbl = statSummaryLbl

    -- 3. Filter Bar & Action Buttons
    local controlRow = wm:CreateControl("$(parent)_Controls", card, CT_CONTROL)
    controlRow:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 34)
    controlRow:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 34)
    controlRow:SetHeight(32)

    -- Filter Buttons: All, Changes Only, Promotes, Demotes
    local filters = {
        { id = "all", label = "All" },
        { id = "changes", label = "Changes Only" },
        { id = "promote", label = "Promotions" },
        { id = "demote", label = "Demotions" },
    }
    self.autoRankFilterBtns = {}

    local curX = 0
    for _, f in ipairs(filters) do
        local btn = wm:CreateControl("$(parent)_F_" .. f.id, controlRow, CT_BUTTON)
        btn:SetAnchor(TOPLEFT, controlRow, TOPLEFT, curX, 3)
        btn:SetDimensions(86, 24)
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
            self:UpdateAutoRanksFilterButtons()
            self:RefreshAutoRanksGrid()
        end)
        self.autoRankFilterBtns[f.id] = btn
        curX = curX + 90
    end

    -- Lookback Window cycle button
    local windowBtn = wm:CreateControl("$(parent)_WindowBtn", controlRow, CT_BUTTON)
    windowBtn:SetAnchor(TOPLEFT, controlRow, TOPLEFT, curX + 10, 3)
    windowBtn:SetDimensions(80, 24)
    windowBtn:SetFont("ZoFontGameSmall")
    windowBtn:SetText("10 Days")
    self:StyleTactileButton(windowBtn, {
        normalBg = { 0.10, 0.08, 0.04, 0.85 },
        hoverBg = { 0.18, 0.14, 0.06, 0.95 },
        normalEdge = { 0.60, 0.45, 0.15, 0.70 },
        hoverEdge = { 0.95, 0.70, 0.20, 1.0 },
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

    -- Right Action Buttons: Evaluate, Apply Changes, Abort
    local evalBtn = wm:CreateControl("$(parent)_EvalBtn", controlRow, CT_BUTTON)
    evalBtn:SetAnchor(TOPRIGHT, controlRow, TOPRIGHT, -180, 2)
    evalBtn:SetDimensions(90, 26)
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

    local applyBtn = wm:CreateControl("$(parent)_ApplyBtn", controlRow, CT_BUTTON)
    applyBtn:SetAnchor(TOPRIGHT, controlRow, TOPRIGHT, -8, 2)
    applyBtn:SetDimensions(165, 26)
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
    abortBtn:SetAnchor(TOPRIGHT, controlRow, TOPRIGHT, -8, 2)
    abortBtn:SetDimensions(165, 26)
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

    local progLbl = wm:CreateControl("$(parent)_ProgLbl", controlRow, CT_LABEL)
    progLbl:SetAnchor(RIGHT, applyBtn, LEFT, -12, 0)
    progLbl:SetFont("ZoFontGameSmall")
    progLbl:SetText("")
    self.autoRanksProgressLbl = progLbl

    -- 4. Table Header Row
    local headerRow = wm:CreateControl("$(parent)_Header", card, CT_BACKDROP)
    headerRow:SetAnchor(TOPLEFT, card, TOPLEFT, 12, 70)
    headerRow:SetAnchor(TOPRIGHT, card, TOPRIGHT, -12, 70)
    headerRow:SetHeight(26)
    headerRow:SetCenterColor(0.04, 0.04, 0.06, 0.95)
    headerRow:SetEdgeColor(0.25, 0.25, 0.30, 0.50)
    headerRow:SetEdgeTexture("", 1, 1, 0)

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
        for _, item in ipairs(self.autoRankResults or {}) do
            if item.action ~= "KEEP" then
                item.selected = masterCheck.allSelected
            end
        end
        self:RefreshAutoRanksGrid()
    end)

    local function MakeHdrLbl(name, anchorCtrl, anchorPoint, toPoint, x, width, text)
        local l = wm:CreateControl("$(parent)_" .. name, headerRow, CT_LABEL)
        l:SetAnchor(anchorPoint, anchorCtrl, toPoint, x, 0)
        l:SetDimensions(width, 22)
        l:SetFont("ZoFontGameBold")
        l:SetColor(0.75, 0.75, 0.80, 1)
        l:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        l:SetText(text)
        return l
    end

    local hMember = MakeHdrLbl("HMember", masterCheck, LEFT, RIGHT, 8, 160, "Member (@Name)")
    local hCurRank = MakeHdrLbl("HCurRank", hMember, LEFT, RIGHT, 6, 120, "Current Rank")
    local hTgtRank = MakeHdrLbl("HTgtRank", hCurRank, LEFT, RIGHT, 6, 120, "Target Rank")
    local hAction = MakeHdrLbl("HAction", hTgtRank, LEFT, RIGHT, 6, 85, "Action")
    local hSales = MakeHdrLbl("HSales", hAction, LEFT, RIGHT, 6, 100, "Sales Gold")
    local hDep = MakeHdrLbl("HDep", hSales, LEFT, RIGHT, 6, 85, "Bank Dues")
    local hNote = MakeHdrLbl("HNote", hDep, LEFT, RIGHT, 6, 170, "Assessment / Note")

    -- 5. Scrollable Data Grid
    local scrollContainer = wm:CreateControl("$(parent)_Scroll", card, CT_SCROLL)
    scrollContainer:SetAnchor(TOPLEFT, headerRow, BOTTOMLEFT, 0, 4)
    scrollContainer:SetAnchor(BOTTOMRIGHT, card, BOTTOMRIGHT, -12, -10)

    self.autoRanksGridRows = {}
    self.autoRanksScrollContainer = scrollContainer

    -- Pre-create row pool (up to 20 rows visible at once, expandable)
    for r = 1, 24 do
        local row = wm:CreateControl("$(parent)_Row" .. r, scrollContainer, CT_CONTROL)
        row:SetAnchor(TOPLEFT, scrollContainer, TOPLEFT, 0, (r - 1) * 24)
        row:SetAnchor(TOPRIGHT, scrollContainer, TOPRIGHT, 0, (r - 1) * 24)
        row:SetHeight(23)

        local rowBg = wm:CreateControl("$(parent)_Bg", row, CT_BACKDROP)
        rowBg:SetAnchorFill()
        rowBg:SetCenterColor(0.04, 0.04, 0.06, 0.50)
        rowBg:SetEdgeColor(0.15, 0.15, 0.20, 0.40)
        rowBg:SetEdgeTexture("", 1, 1, 0)

        local checkBtn = wm:CreateControl("$(parent)_Check", row, CT_BUTTON)
        checkBtn:SetAnchor(LEFT, row, LEFT, 8, 0)
        checkBtn:SetDimensions(20, 20)
        checkBtn:SetFont("ZoFontGameSmall")
        checkBtn:SetText("[✓]")

        local memberLbl = wm:CreateControl("$(parent)_Member", row, CT_LABEL)
        memberLbl:SetAnchor(LEFT, checkBtn, RIGHT, 8, 0)
        memberLbl:SetDimensions(160, 22)
        memberLbl:SetFont("ZoFontGameSmall")
        memberLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)

        local curRankLbl = wm:CreateControl("$(parent)_CurRank", row, CT_LABEL)
        curRankLbl:SetAnchor(LEFT, memberLbl, RIGHT, 6, 0)
        curRankLbl:SetDimensions(120, 22)
        curRankLbl:SetFont("ZoFontGameSmall")
        curRankLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)

        local tgtRankLbl = wm:CreateControl("$(parent)_TgtRank", row, CT_LABEL)
        tgtRankLbl:SetAnchor(LEFT, curRankLbl, RIGHT, 6, 0)
        tgtRankLbl:SetDimensions(120, 22)
        tgtRankLbl:SetFont("ZoFontGameSmall")
        tgtRankLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)

        local actLbl = wm:CreateControl("$(parent)_Act", row, CT_LABEL)
        actLbl:SetAnchor(LEFT, tgtRankLbl, RIGHT, 6, 0)
        actLbl:SetDimensions(85, 22)
        actLbl:SetFont("ZoFontGameSmall")
        actLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)

        local salesLbl = wm:CreateControl("$(parent)_Sales", row, CT_LABEL)
        salesLbl:SetAnchor(LEFT, actLbl, RIGHT, 6, 0)
        salesLbl:SetDimensions(100, 22)
        salesLbl:SetFont("ZoFontGameSmall")
        salesLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)

        local depLbl = wm:CreateControl("$(parent)_Dep", row, CT_LABEL)
        depLbl:SetAnchor(LEFT, salesLbl, RIGHT, 6, 0)
        depLbl:SetDimensions(85, 22)
        depLbl:SetFont("ZoFontGameSmall")
        depLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)

        local noteLbl = wm:CreateControl("$(parent)_Note", row, CT_LABEL)
        noteLbl:SetAnchor(LEFT, depLbl, RIGHT, 6, 0)
        noteLbl:SetAnchor(RIGHT, row, RIGHT, -8, 0)
        noteLbl:SetFont("ZoFontGameSmall")
        noteLbl:SetVerticalAlignment(TEXT_ALIGN_CENTER)
        noteLbl:SetWrapMode(TEXT_WRAP_MODE_ELLIPSIS)

        self.autoRanksGridRows[r] = {
            row = row,
            bg = rowBg,
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

    self:UpdateAutoRanksFilterButtons()
end

function FR:UpdateAutoRanksFilterButtons()
    for id, btn in pairs(self.autoRankFilterBtns or {}) do
        if btn.bg then
            if id == self.autoRankFilter then
                btn.bg:SetCenterColor(0.04, 0.18, 0.16, 0.95)
                btn.bg:SetEdgeColor(0, 0.90, 0.80, 1.0)
                btn:SetNormalFontColor(0, 1, 0.85, 1)
            else
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

    for _, item in ipairs(self.autoRankResults or {}) do
        if item.action == "PROMOTE" then pCount = pCount + 1
        elseif item.action == "DEMOTE" then dCount = dCount + 1
        else kCount = kCount + 1 end

        local match = false
        if filter == "all" then match = true
        elseif filter == "changes" and item.action ~= "KEEP" then match = true
        elseif filter == "promote" and item.action == "PROMOTE" then match = true
        elseif filter == "demote" and item.action == "DEMOTE" then match = true end

        if match then table.insert(filtered, item) end
    end

    if self.autoRanksSummaryLbl then
        self.autoRanksSummaryLbl:SetText(string.format("Evaluated: |cFFFFFF%d|r  |  |c59E08APromote: %d|r  |  |cFF6666Demote: %d|r  |  |c888888Kept: %d|r",
            #self.autoRankResults, pCount, dCount, kCount))
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

    for r = 1, #self.autoRanksGridRows do
        local rCtrl = self.autoRanksGridRows[r]
        local item = filtered[r]

        if rCtrl and item then
            rCtrl.row:SetHidden(false)

            if item.action == "KEEP" then
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
            rCtrl.tgtRank:SetText(string.format("%s%s|r",
                item.action == "PROMOTE" and "|c59E08A" or (item.action == "DEMOTE" and "|cFF6666" or "|c888888"),
                item.targetRankName))

            local actText = "|c888888[KEEP]|r"
            if item.action == "PROMOTE" then actText = "|c59E08A[PROMOTE]|r"
            elseif item.action == "DEMOTE" then actText = "|cFF6666[DEMOTE]|r" end
            rCtrl.action:SetText(actText)

            rCtrl.sales:SetText(item.salesGold > 0 and string.format("|cFFD700%sg|r", ZO_CommaDelimitNumber(item.salesGold)) or "|c555555--|r")
            rCtrl.dep:SetText(item.donations > 0 and string.format("|c59E08A%sg|r", ZO_CommaDelimitNumber(item.donations)) or "|c555555--|r")
            rCtrl.note:SetText(string.format("|c999999%s%s|r", item.status or "", (item.note ~= "" and " (" .. item.note .. ")") or ""))

            if r % 2 == 0 then
                rCtrl.bg:SetCenterColor(0.05, 0.05, 0.08, 0.60)
            else
                rCtrl.bg:SetCenterColor(0.03, 0.03, 0.05, 0.40)
            end
        elseif rCtrl then
            rCtrl.row:SetHidden(true)
        end
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
    self:RefreshAutoRanksGrid()
end

--[[ =========================================================================
     SLASH COMMANDS & NAVIGATION
========================================================================= ]]--

function FR:OpenAutoRanksConsole()
    if self.ToggleConsoleWindow then
        self:ToggleConsoleWindow(true)
    end
    if self.SelectConsoleTab then
        self:SelectConsoleTab(6)
    end
end

SLASH_COMMANDS["/autoranks"] = function() FR:OpenAutoRanksConsole() end
SLASH_COMMANDS["/ar"] = function() FR:OpenAutoRanksConsole() end
