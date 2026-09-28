-- The studio scene: one client-side model posed frame by frame (never animated by the game clock), so the
-- preview and every rendered frame are exactly reproducible.
local V, Rig, Acc = LQS.V, LQS.Rig, LQS.Acc

local S = { ent = nil, rig = {}, bones = {}, flexOpen = {}, convention = nil }
local F, L, U = { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 }

-- Mouth flexes by name, most specific first. The second group are viseme-style names (VRChat/MMD ports).
local MOUTH_FLEX = { "jaw_drop", "mouth_drop", "jaw_open", "mouthopen", "mouth_open", "open_mouth" }
local VISEME_FLEX = { "vrc.v_aa", "v_aa", "mth_a", "aa", "a" }

local function toM3(vm)
    local t = vm:ToTable()
    return V.orthonormal({ t[1][1], t[1][2], t[1][3], t[2][1], t[2][2], t[2][3], t[3][1], t[3][2], t[3][3] })
end

local function toAngle(m)
    local vm = Matrix({ { m[1], m[2], m[3], 0 }, { m[4], m[5], m[6], 0 }, { m[7], m[8], m[9], 0 }, { 0, 0, 0, 1 } })
    return vm:GetAngles()
end

local function vec(v) return { v.x, v.y, v.z } end

function S.Remove()
    if IsValid(S.ent) then S.ent:Remove() end
    S.ent = nil
end

-- Loads a model and analyses what can be animated. Returns ok, report text.
function S.Load(model)
    S.Remove()
    if not model or model == "" or not util.IsValidModel(model) then
        return false, "No se encuentra el modelo «" .. tostring(model) .. "». Copia la ruta desde el menú Q (clic derecho → Copiar al portapapeles)."
    end
    local ent = ClientsideModel(model, RENDERGROUP_OTHER)
    if not IsValid(ent) then return false, "Garry's Mod no pudo crear el modelo." end
    ent:SetNoDraw(true)
    ent:SetIK(false)
    ent:SetPos(Vector(0, 0, 0))
    ent:SetAngles(Angle(0, 0, 0))
    S.ent, S.model, S.convention, S.touched = ent, model, nil, {}

    local bones = {}
    for i = 0, ent:GetBoneCount() - 1 do
        local name = ent:GetBoneName(i)
        if name and name ~= "__INVALIDBONE__" then
            bones[#bones + 1] = { id = i, name = name, parent = ent:GetBoneParent(i) }
        end
    end
    S.bones = bones
    S.rig = Rig.detect(bones)

    S.flexOpen, S.flexNames = {}, {}
    for i = 0, ent:GetFlexNum() - 1 do
        S.flexNames[i] = ent:GetFlexName(i)
    end
    for _, group in ipairs({ MOUTH_FLEX, VISEME_FLEX }) do
        for i, name in pairs(S.flexNames) do
            local lower = string.lower(name)
            for _, w in ipairs(group) do
                if (group == MOUTH_FLEX and string.find(lower, w, 1, true)) or lower == w then
                    S.flexOpen[#S.flexOpen + 1] = i
                    break
                end
            end
        end
        if #S.flexOpen > 0 then break end
    end

    local mins, maxs = ent:GetModelBounds()
    S.mins, S.maxs = mins, maxs
    S.height = math.max(8, maxs.z - mins.z)
    S.baseSeq = S.FindSequence("ACT_HL2MP_IDLE") or ent:LookupSequence("idle_all_01")
    if not S.baseSeq or S.baseSeq < 0 then S.baseSeq = ent:LookupSequence("idle") end
    if not S.baseSeq or S.baseSeq < 0 then S.baseSeq = 0 end
    return true, S.Report()
end

function S.FindSequence(actName)
    if not IsValid(S.ent) or not actName then return nil end
    local act = _G[actName]
    if not act then return nil end
    local seq = S.ent:SelectWeightedSequence(act)
    if seq and seq >= 0 then return seq end
    return nil
end

function S.Report()
    local found, missing = {}, {}
    for _, key in ipairs(Rig.orderedKeys()) do
        if S.rig[key] then found[#found + 1] = Rig.label(key) else missing[#missing + 1] = Rig.label(key) end
    end
    local lines = {
        S.model,
        string.format("%d huesos · %d flexes (expresiones) · %d secuencias propias",
            #S.bones, table.Count(S.flexNames), #(S.ent:GetSequenceList() or {})),
        "",
        "Articulaciones reconocidas (" .. #found .. "): " .. (#found > 0 and table.concat(found, ", ") or "ninguna"),
    }
    if #missing > 0 then lines[#lines + 1] = "Sin reconocer: " .. table.concat(missing, ", ") end
    lines[#lines + 1] = ""
    local ok, partial, own = {}, {}, {}
    for _, a in ipairs(Acc.LIST) do
        local miss = 0
        for _, k in ipairs(Acc.NEEDS[a.id] or {}) do if not S.rig[k] then miss = miss + 1 end end
        if miss == 0 then ok[#ok + 1] = a.nombre elseif miss < #(Acc.NEEDS[a.id] or {}) then partial[#partial + 1] = a.nombre end
        if S.FindSequence(Acc.ACTIVITY[a.id]) then own[#own + 1] = a.nombre end
    end
    lines[#lines + 1] = "Acciones por código: " .. (#ok > 0 and table.concat(ok, ", ") or "ninguna completa")
    if #partial > 0 then lines[#lines + 1] = "Limitadas (faltan huesos): " .. table.concat(partial, ", ") end
    if #own > 0 then lines[#lines + 1] = "Con animación propia del modelo: " .. table.concat(own, ", ") end
    lines[#lines + 1] = ""
    if #S.flexOpen > 0 then
        lines[#lines + 1] = "Lipsync: por expresión facial (" .. S.flexNames[S.flexOpen[1]] .. (#S.flexOpen > 1 and " y " .. (#S.flexOpen - 1) .. " más" or "") .. ")."
    elseif S.rig.jaw then
        lines[#lines + 1] = "Lipsync: moviendo el hueso de la mandíbula."
    else
        lines[#lines + 1] = "Lipsync: este modelo no tiene boca animable (ni flexes ni mandíbula)."
    end
    return table.concat(lines, "\n")
end

-- World rotations of every bone at the base pose (manipulations cleared).
local function readBase(ent)
    ent:InvalidateBoneCache()
    ent:SetupBones()
    local rot, pos = {}, {}
    for _, b in ipairs(S.bones) do
        local m = ent:GetBoneMatrix(b.id)
        if m then
            rot[b.id] = toM3(m)
            pos[b.id] = vec(m:GetTranslation())
        end
    end
    return rot, pos
end

local function clearManips(ent)
    for id in pairs(S.touched or {}) do ent:ManipulateBoneAngles(id, Angle(0, 0, 0)) end
    S.touched = {}
end

-- Local manipulation that makes bone `id` rotate by world delta Q (expressed in the base frame).
local function manipFor(id, Q, baseRot)
    local W
    if S.convention == "parent" then
        local parent = S.ent:GetBoneParent(id)
        W = (parent and parent >= 0 and baseRot[parent]) or V.I
    else
        W = baseRot[id]
    end
    return V.mul(V.mul(V.t(W), Q), W)
end

-- GMod applies ManipulateBoneAngles in the bone's own space (documented as "relative to the original bone
-- angle"); some builds behave as parent space. Measure once per model instead of trusting either.
local function calibrate(ent)
    S.convention = "local"
    local probe, child
    for key, childKey in pairs(Rig.AIM_CHILD) do
        for _, side in ipairs({ "_R", "_L", "" }) do
            if S.rig[key .. side] and S.rig[childKey .. side] then
                probe, child = S.rig[key .. side], S.rig[childKey .. side]
                break
            end
        end
        if probe then break end
    end
    if not probe then return end
    local best, bestErr = "local", math.huge
    for _, convention in ipairs({ "local", "parent" }) do
        clearManips(ent)
        local rot, pos = readBase(ent)
        if not rot[probe] or not pos[child] then return end
        local d = V.add(pos[child], V.scale(pos[probe], -1))
        local Q = V.axisAngle(V.norm({ 0.3, 0.8, 0.5 }), 35)
        S.convention = convention
        ent:ManipulateBoneAngles(probe, toAngle(manipFor(probe, Q, rot)))
        S.touched[probe] = true
        local rot2, pos2 = readBase(ent)
        local got = V.add(pos2[child], V.scale(pos2[probe], -1))
        local err = V.angleBetween(got, V.vec(Q, d))
        if err < bestErr then best, bestErr = convention, err end
    end
    clearManips(ent)
    S.convention = best
    S.calibrationError = bestErr
end

-- Poses the model for time t. st = studio state (see ui.lua). Returns the mouth value used.
function S.Pose(st, t)
    local ent = S.ent
    if not IsValid(ent) then return 0 end
    ent:SetSkin(st.skin or 0)
    S.mirar = st.mirar

    -- 1) Base: the model's own animation (when chosen and available) or its idle / reference pose.
    local seq, cycle, useOwn = S.baseSeq, 0, false
    if st.usarPropia then
        local own = st.secuencia or S.FindSequence(Acc.ACTIVITY[st.accion])
        if own and own >= 0 then
            seq, useOwn = own, true
            local len = math.max(0.05, ent:SequenceDuration(own))
            cycle = ((t * st.vel) / len) % 1
        end
    end
    if ent:GetSequence() ~= seq then ent:ResetSequence(seq) end
    ent:SetCycle(cycle)
    if ent:LookupPoseParameter("move_x") >= 0 then ent:SetPoseParameter("move_x", useOwn and 1 or 0) end
    if ent:LookupPoseParameter("move_y") >= 0 then ent:SetPoseParameter("move_y", 0) end

    if not S.convention then calibrate(ent) end
    clearManips(ent)
    local baseRot, basePos = readBase(ent)

    -- 2) Procedural action (only when the model's own animation is not playing).
    local action = { aims = {}, rots = {}, lift = 0 }
    if not useOwn then
        action = Acc.evaluate(st.accion, t, { vel = st.vel, amp = st.amp, dur = st.dur })
    end

    -- 3) Lip sync.
    local mouth = 0
    if st.boca then
        local i = math.floor(t * st.fps) + 1
        mouth = st.boca[math.min(#st.boca, math.max(1, i))] or 0
        if i > #st.boca then mouth = 0 end
    end
    if mouth > 0 and #S.flexOpen == 0 and S.rig.jaw then
        action.rots[#action.rots + 1] = { "jaw", L, 22 * mouth }
    end

    -- 4) Solve each role in hierarchy order: D = world delta of each processed bone.
    local delta = {}
    local function parentDelta(id)
        local p, guard = ent:GetBoneParent(id), 0
        while p and p >= 0 and guard < 256 do
            if delta[p] then return delta[p] end
            p, guard = ent:GetBoneParent(p), guard + 1
        end
        return V.I
    end
    for _, key in ipairs(Rig.orderedKeys()) do
        local id = S.rig[key]
        if id and baseRot[id] then
            local Dp = parentDelta(id)
            local Q = V.I
            local childKey = Rig.AIM_CHILD[string.match(key, "^(%a+)")]
            local childId = childKey and S.rig[childKey .. (string.match(key, "(_[RL])$") or "")]
            local d
            if childId and basePos[childId] then d = V.norm(V.add(basePos[childId], V.scale(basePos[id], -1))) end
            local target = action.aims[key]
            if target and d then
                Q = V.between(d, V.vec(V.t(Dp), target))
            end
            for _, r in ipairs(action.rots) do
                if r[1] == key then Q = V.mul(V.axisAngle(r[2], r[3]), Q) end
            end
            -- Manual adjustments of the pose designer: forward/back, sideways, twist, measured on the
            -- bone's CURRENT direction so "forward" means forward whatever the rest pose (T, A, idle).
            local man = st.pose and st.pose[key]
            if man and (man[1] ~= 0 or man[2] ~= 0 or man[3] ~= 0) then
                local cur = d and V.vec(Q, d) or U
                local side = string.match(key, "_([RL])$")
                local out = side == "R" and { 0, -1, 0 } or side == "L" and { 0, 1, 0 } or L
                local sF = V.dot(V.cross(L, cur), F) >= 0 and 1 or -1
                local sS = V.dot(V.cross(F, cur), out) >= 0 and 1 or -1
                if man[1] ~= 0 then Q = V.mul(V.axisAngle(L, man[1] * sF), Q) end
                if man[2] ~= 0 then Q = V.mul(V.axisAngle(F, man[2] * sS), Q) end
                if man[3] ~= 0 then Q = V.mul(V.axisAngle(cur, man[3]), Q) end
            end
            if Q ~= V.I then
                ent:ManipulateBoneAngles(id, toAngle(manipFor(id, Q, baseRot)))
                S.touched[id] = true
            end
            delta[id] = V.mul(Dp, Q)
        end
    end

    -- 5) Face: expression sliders, mouth, eyes.
    for i = 0, ent:GetFlexNum() - 1 do
        local w = st.flex and st.flex[i] or 0
        ent:SetFlexWeight(i, w)
    end
    for _, i in ipairs(S.flexOpen) do
        local lo, hi = ent:GetFlexBounds(i)
        local expr = st.flex and st.flex[i] or 0
        ent:SetFlexWeight(i, math.max(expr, lo + (hi - lo) * mouth))
    end

    ent:SetPos(Vector(0, 0, (action.lift or 0) * S.height))
    ent:InvalidateBoneCache()
    return mouth
end

-- Camera: orbit around a framing point. Returns pos, ang.
-- aspect = width / height of the frame. The FOV is horizontal, so the vertical span shrinks on wide frames.
function S.Camera(cam, aspect)
    local h = S.height or 72
    local base = (S.mins and S.mins.z or 0)
    local centerZ = base + h * (cam.encuadre == "primer" and 0.9 or cam.encuadre == "medio" and 0.72 or 0.5)
    if cam.encuadre == "primer" and IsValid(S.ent) and S.rig.head then
        local m = S.ent:GetBoneMatrix(S.rig.head)
        if m then centerZ = m:GetTranslation().z + h * 0.03 end
    end
    local span = h * (cam.encuadre == "primer" and 0.3 or cam.encuadre == "medio" and 0.6 or 1.12)
    local tanV = math.tan(math.rad(cam.fov / 2)) / math.max(0.2, aspect or 1)
    local dist = (span / 2) / tanV * cam.zoom
    local ang = Angle(cam.pitch, cam.yaw, 0)
    local center = Vector(0, 0, centerZ + cam.altura * h)
    return center - ang:Forward() * dist, ang
end

-- Draws the model with a studio light that follows the camera (no map lighting).
function S.Draw(camPos, camAng, fov, x, y, w, h)
    if not IsValid(S.ent) then return end
    cam.Start3D(camPos, camAng, fov, x, y, w, h, 1, 100000)
    render.SuppressEngineLighting(true)
    render.SetLightingOrigin(S.ent:GetPos())
    render.ResetModelLighting(0.32, 0.32, 0.35)
    local key = (-camAng:Forward() + Vector(0, 0, 0.7) + camAng:Right() * 0.35):GetNormalized()
    local boxes = {
        [BOX_FRONT] = Vector(1, 0, 0), [BOX_BACK] = Vector(-1, 0, 0), [BOX_LEFT] = Vector(0, 1, 0),
        [BOX_RIGHT] = Vector(0, -1, 0), [BOX_TOP] = Vector(0, 0, 1), [BOX_BOTTOM] = Vector(0, 0, -1),
    }
    for box, n in pairs(boxes) do
        local k = 0.3 + 0.85 * math.max(0, n:Dot(key))
        render.SetModelLighting(box, k, k, k * 0.98)
    end
    render.SetColorModulation(1, 1, 1)
    render.SetBlend(1)
    if S.mirar then S.ent:SetEyeTarget(camPos) end
    S.ent:DrawModel()
    render.SuppressEngineLighting(false)
    cam.End3D()
end

return S
