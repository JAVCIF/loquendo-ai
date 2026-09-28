-- Preset actions performed IN PLACE (the character moves but does not travel), as pure functions of time.
-- Model space: +X forward, +Y left, +Z up. An action returns
--   aims[roleKey] = direction the bone must point to (towards its chain child), in model space;
--   rots = { {key, axis, deg}, ... } extra rotations (head nods, leaning...);
--   lift = vertical offset of the whole model, as a fraction of its height (jumps, bobbing).
-- The engine adapts these to each skeleton: it only uses the roles the model has.
local V = include("loquendo_studio/mat3.lua")
local A = {}

local F, L, U = { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 }
local TAU = math.pi * 2

local function dir(x, y, z) return V.norm({ x, y, z }) end
local function smooth(e0, e1, x)
    local t = math.max(0, math.min(1, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)
end
-- 0 → 1 → 0 envelope for one-shot gestures of length dur (in/out of `edge` seconds).
local function envelope(t, dur, edge)
    edge = math.min(edge or 0.35, dur / 2)
    return smooth(0, edge, t) * (1 - smooth(dur - edge, dur, t))
end
local function bump(u, a, b) -- 0 outside [a,b], sine hump inside
    if u < a or u > b then return 0 end
    return math.sin(math.pi * (u - a) / (b - a))
end

-- Neutral standing pose: arms relaxed along the body, legs straight. s = -1 right side, +1 left side.
local function neutral(out)
    for _, side in ipairs({ "R", "L" }) do
        local s = side == "R" and -1 or 1
        out.aims["upperarm_" .. side] = dir(0.04, s * 0.2, -1)
        out.aims["forearm_" .. side] = dir(0.28, s * 0.06, -1)
        out.aims["thigh_" .. side] = dir(0, s * 0.05, -1)
        out.aims["calf_" .. side] = dir(-0.02, s * 0.02, -1)
    end
    out.aims.spine = dir(0, 0, 1)
    out.aims.chest = dir(0, 0, 1)
    out.aims.neck = dir(0.04, 0, 1)
end

local function blendTowards(out, key, target, w)
    local current = out.aims[key]
    out.aims[key] = current and V.mix(current, target, w) or target
end

local function rot(out, key, axis, deg) out.rots[#out.rots + 1] = { key, axis, deg } end

A.LIST = {
    {
        id = "respirar", nombre = "Respirar (quieto)", bucle = true, dur = 4,
        eval = function(out, t, p)
            local b = math.sin(TAU * t / 3.6)
            rot(out, "chest", L, -1.5 * b * p.amp)
            rot(out, "head", L, 1.2 * math.sin(TAU * t / 5.1) * p.amp)
            for _, side in ipairs({ "R", "L" }) do
                local s = side == "R" and -1 or 1
                out.aims["upperarm_" .. side] = dir(0.04 + 0.02 * b, s * (0.2 + 0.02 * b), -1)
            end
        end,
    },
    {
        id = "hablar", nombre = "Hablar (gesticular)", bucle = true, dur = 4,
        eval = function(out, t, p)
            local a = p.amp
            out.aims.forearm_R = dir(0.75 + 0.25 * math.sin(TAU * 0.55 * t), -0.25 - 0.15 * math.sin(TAU * 0.9 * t), -0.55 + 0.3 * math.sin(TAU * 0.7 * t) * a)
            out.aims.upperarm_R = dir(0.12, -0.28, -1)
            out.aims.forearm_L = dir(0.6 + 0.2 * math.sin(TAU * 0.43 * t + 1.3), 0.2 + 0.12 * math.sin(TAU * 0.8 * t + 2), -0.7 + 0.25 * math.sin(TAU * 0.6 * t + 0.4) * a)
            out.aims.upperarm_L = dir(0.08, 0.26, -1)
            rot(out, "head", L, (3 * math.sin(TAU * 0.9 * t) + 2 * math.sin(TAU * 1.7 * t)) * a)
            rot(out, "head", U, 4 * math.sin(TAU * 0.35 * t) * a)
            rot(out, "chest", U, 3 * math.sin(TAU * 0.3 * t) * a)
        end,
    },
    {
        id = "caminar", nombre = "Caminar en el sitio", bucle = true, dur = 4,
        eval = function(out, t, p)
            local ph = TAU * 0.95 * p.vel * t
            for _, side in ipairs({ "R", "L" }) do
                local s = side == "R" and -1 or 1
                local phase = ph + (side == "R" and 0 or math.pi)
                local lift = math.max(0, math.sin(phase))
                out.aims["thigh_" .. side] = dir(0.75 * lift * p.amp, s * 0.06, -1)
                out.aims["calf_" .. side] = dir(-0.2 * lift, s * 0.02, -1)
                local swing = -math.sin(phase) * 0.35 * p.amp
                out.aims["upperarm_" .. side] = dir(swing, s * 0.2, -1)
                out.aims["forearm_" .. side] = dir(0.35 + math.max(0, swing) * 0.6, s * 0.05, -1)
            end
            out.lift = 0.012 * math.abs(math.sin(ph)) * p.amp
            rot(out, "chest", U, 4 * math.sin(ph) * p.amp)
        end,
    },
    {
        id = "correr", nombre = "Correr en el sitio", bucle = true, dur = 3,
        eval = function(out, t, p)
            local ph = TAU * 1.55 * p.vel * t
            for _, side in ipairs({ "R", "L" }) do
                local s = side == "R" and -1 or 1
                local phase = ph + (side == "R" and 0 or math.pi)
                local lift = math.max(0, math.sin(phase))
                local back = math.max(0, -math.sin(phase))
                out.aims["thigh_" .. side] = dir(1.25 * lift * p.amp - 0.25 * back, s * 0.06, -1 + 0.25 * lift)
                out.aims["calf_" .. side] = dir(-0.35 - 0.9 * back * p.amp, s * 0.02, -1)
                local swing = -math.sin(phase) * 0.75 * p.amp
                out.aims["upperarm_" .. side] = dir(swing, s * 0.22, -1)
                out.aims["forearm_" .. side] = dir(1, s * 0.12, 0.05 + 0.35 * math.max(0, swing))
            end
            out.lift = 0.03 * math.abs(math.sin(ph)) * p.amp
            rot(out, "spine", L, 8 * p.amp)
            rot(out, "chest", U, 6 * math.sin(ph) * p.amp)
        end,
    },
    {
        id = "saltar", nombre = "Saltar", bucle = true, dur = 1.3,
        eval = function(out, t, p)
            local T = 1.3 / p.vel
            local u = (t % T) / T
            local crouch = bump(u, 0, 0.36) + bump(u, 0.78, 1) * 0.8
            local air = bump(u, 0.3, 0.82)
            for _, side in ipairs({ "R", "L" }) do
                local s = side == "R" and -1 or 1
                out.aims["thigh_" .. side] = dir(0.75 * crouch + 0.35 * air * p.amp, s * 0.07, -1)
                out.aims["calf_" .. side] = dir(-0.75 * crouch - 0.3 * air, s * 0.02, -1)
                out.aims["upperarm_" .. side] = dir(-0.6 * crouch + 0.5 * air, s * (0.25 + 0.2 * air), -1 + 1.7 * air * p.amp)
                out.aims["forearm_" .. side] = dir(0.3 + 0.3 * air, s * 0.1, -1 + 1.8 * air * p.amp)
            end
            rot(out, "spine", L, 14 * crouch)
            rot(out, "head", L, -6 * crouch)
            out.lift = 0.22 * air * p.amp - 0.09 * crouch
        end,
    },
    {
        id = "saludar", nombre = "Saludar", bucle = false, dur = 2.5,
        eval = function(out, t, p)
            local w = envelope(t, p.dur, 0.4)
            local wave = math.sin(TAU * 1.7 * p.vel * t) * 0.55 * p.amp
            blendTowards(out, "upperarm_R", dir(0.15, -0.8, 0.6), w)
            blendTowards(out, "forearm_R", dir(0.15, -wave, 1), w)
            rot(out, "head", F, -5 * w)
            rot(out, "chest", F, -3 * w)
        end,
    },
    {
        id = "asentir", nombre = "Asentir (sí)", bucle = false, dur = 1.6,
        eval = function(out, t, p)
            local w = envelope(t, p.dur, 0.2)
            rot(out, "head", L, 13 * math.sin(TAU * 2 * p.vel * t) * p.amp * w)
            rot(out, "neck", L, 4 * math.sin(TAU * 2 * p.vel * t) * p.amp * w)
        end,
    },
    {
        id = "negar", nombre = "Negar (no)", bucle = false, dur = 1.6,
        eval = function(out, t, p)
            local w = envelope(t, p.dur, 0.2)
            rot(out, "head", U, 20 * math.sin(TAU * 1.8 * p.vel * t) * p.amp * w)
            rot(out, "neck", U, 5 * math.sin(TAU * 1.8 * p.vel * t) * p.amp * w)
        end,
    },
    {
        id = "celebrar", nombre = "Celebrar", bucle = true, dur = 3,
        eval = function(out, t, p)
            local pump = math.abs(math.sin(TAU * 1.4 * p.vel * t))
            for _, side in ipairs({ "R", "L" }) do
                local s = side == "R" and -1 or 1
                out.aims["upperarm_" .. side] = dir(0.12, s * (0.45 + 0.15 * pump), 1)
                out.aims["forearm_" .. side] = dir(0.1, s * 0.15, 1)
            end
            out.lift = 0.05 * pump * p.amp
            rot(out, "head", L, -8)
        end,
    },
    {
        id = "senalar", nombre = "Señalar al frente", bucle = false, dur = 2.5,
        eval = function(out, t, p)
            local w = envelope(t, p.dur, 0.35)
            blendTowards(out, "upperarm_R", dir(1, -0.18, 0.12), w)
            blendTowards(out, "forearm_R", dir(1, -0.1, 0.15), w)
            rot(out, "chest", U, 6 * w)
        end,
    },
    {
        id = "hombros", nombre = "Encoger hombros (no sé)", bucle = false, dur = 2,
        eval = function(out, t, p)
            local w = envelope(t, p.dur, 0.35) * p.amp
            for _, side in ipairs({ "R", "L" }) do
                local s = side == "R" and -1 or 1
                rot(out, "clavicle_" .. side, F, s * 14 * w)
                blendTowards(out, "upperarm_" .. side, dir(0.2, s * 0.3, -1), w)
                blendTowards(out, "forearm_" .. side, dir(0.8, s * 0.65, 0.05), w)
            end
            rot(out, "head", F, 9 * w)
        end,
    },
}

A.BY_ID = {}
for _, a in ipairs(A.LIST) do A.BY_ID[a.id] = a end

-- Evaluates action `id` at time t. p = { vel, amp, dur }.
function A.evaluate(id, t, p)
    local out = { aims = {}, rots = {}, lift = 0 }
    neutral(out)
    local action = A.BY_ID[id]
    if action then action.eval(out, t, p) end
    return out
end

-- Roles an action needs to look right (the report marks it «limitada» when some are missing).
A.NEEDS = {
    respirar = { "upperarm_R", "upperarm_L" },
    hablar = { "upperarm_R", "forearm_R", "head" },
    caminar = { "thigh_R", "thigh_L", "calf_R", "calf_L" },
    correr = { "thigh_R", "thigh_L", "calf_R", "calf_L", "upperarm_R", "upperarm_L" },
    saltar = { "thigh_R", "thigh_L", "calf_R", "calf_L" },
    saludar = { "upperarm_R", "forearm_R" },
    asentir = { "head" },
    negar = { "head" },
    celebrar = { "upperarm_R", "upperarm_L" },
    senalar = { "upperarm_R", "forearm_R" },
    hombros = { "upperarm_R", "upperarm_L" },
}

-- Model animations (activities) preferred when the model has them: player models and HL2 characters.
A.ACTIVITY = {
    respirar = "ACT_HL2MP_IDLE", caminar = "ACT_HL2MP_WALK", correr = "ACT_HL2MP_RUN",
    saludar = "ACT_GMOD_GESTURE_WAVE", asentir = "ACT_GMOD_GESTURE_AGREE", negar = "ACT_GMOD_GESTURE_DISAGREE",
    celebrar = "ACT_GMOD_TAUNT_CHEER", senalar = "ACT_SIGNAL_FORWARD",
}

A.envelope = envelope
return A
