-- Rig detection by bone names (pure Lua). Works with ValveBiped (HL2 / player models), Mixamo, Blender/Rigify
-- style names («upper_arm.L») and most rigs that say what a bone is. Returns the bone of each ROLE and a report.
local R = {}

-- Processing order = parents before children. `side` roles exist twice (R and L).
R.ROLES = {
    { key = "pelvis", label = "Pelvis", words = { "pelvis", "hips", "hip" } },
    { key = "spine", label = "Columna", words = { "spine", "abdomen", "waist" } },
    { key = "chest", label = "Pecho", words = { "upperchest", "chest", "spine", "torso" }, deep = true },
    { key = "neck", label = "Cuello", words = { "neck" } },
    { key = "head", label = "Cabeza", words = { "head" }, exclude = { "end", "top", "nub" } },
    { key = "jaw", label = "Mandíbula", words = { "jaw" } },
    { key = "clavicle", label = "Hombro", side = true, words = { "clavicle", "collar", "shoulder" } },
    { key = "upperarm", label = "Brazo", side = true, words = { "upperarm", "upper_arm", "uparm", "arm" },
        exclude = { "fore", "lower", "low_" } },
    { key = "forearm", label = "Antebrazo", side = true, words = { "forearm", "lowerarm", "lower_arm", "elbow" } },
    { key = "hand", label = "Mano", side = true, words = { "hand", "wrist" } },
    { key = "thigh", label = "Muslo", side = true, words = { "thigh", "upleg", "upperleg", "upper_leg" } },
    { key = "calf", label = "Pierna", side = true, words = { "calf", "shin", "lowerleg", "lower_leg", "knee", "leg" },
        exclude = { "up" } },
    { key = "foot", label = "Pie", side = true, words = { "foot", "ankle" } },
}

-- Which role each role aims at (its "child" in the chain) for direction control.
R.AIM_CHILD = {
    upperarm = "forearm", forearm = "hand", thigh = "calf", calf = "foot",
    spine = "chest", chest = "neck", neck = "head", clavicle = "upperarm",
}

-- Deformation helpers, fingers and the like: never a main role.
local HELPERS = { "twist", "roll", "helper", "ulna", "bicep", "trapezius", "nub", "attach", "jiggle", "finger",
    "thumb", "index", "middle", "ring", "pinky", "toe", "eye", "weapon", "prop", "hlp", "ik_", "_ik", "pole",
    "breast", "hair", "skirt", "cloth", "tail", "wing" }

local function has(text, words)
    for _, w in ipairs(words) do
        if string.find(text, w, 1, true) then return true end
    end
    return false
end

-- "R", "L" or nil from the bone name.
function R.side(name)
    local lower = string.lower(name)
    for token in string.gmatch(lower, "[%w]+") do
        if token == "r" or token == "right" or string.sub(token, 1, 5) == "right" or string.sub(token, -5) == "right" then
            return "R"
        end
        if token == "l" or token == "left" or string.sub(token, 1, 4) == "left" or string.sub(token, -4) == "left" then
            return "L"
        end
    end
    return nil
end

-- bones: array of { id = number, name = string, parent = number (-1 = none) }.
-- Returns roles["upperarm_R"] = id, plus the depth of every bone.
function R.detect(bones)
    local byId, depth = {}, {}
    for _, b in ipairs(bones) do byId[b.id] = b end
    local function depthOf(id)
        if depth[id] then return depth[id] end
        local b, d, guard = byId[id], 0, 0
        while b and b.parent and b.parent >= 0 and byId[b.parent] and guard < 256 do
            d, b, guard = d + 1, byId[b.parent], guard + 1
        end
        depth[id] = d
        return d
    end

    local roles, used = {}, {}
    for _, role in ipairs(R.ROLES) do
        local sides = role.side and { "R", "L" } or { false }
        for _, side in ipairs(sides) do
            local found
            for _, word in ipairs(role.words) do
                local best, bestDepth
                for _, b in ipairs(bones) do
                    local lower = string.lower(b.name)
                    local bSide = R.side(b.name)
                    local sideOk = (side == false and bSide == nil) or (side ~= false and bSide == side)
                    if sideOk and not used[b.id] and string.find(lower, word, 1, true) and not has(lower, HELPERS)
                        and not (role.exclude and has(lower, role.exclude)) then
                        local d = depthOf(b.id)
                        if not best or (role.deep and d > bestDepth) or (not role.deep and d < bestDepth) then
                            best, bestDepth = b, d
                        end
                    end
                end
                if best then found = best break end
            end
            if found then
                roles[R.key(role.key, side or nil)] = found.id
                used[found.id] = true
            end
        end
    end
    return roles, depthOf
end

function R.key(role, side) return side and (role .. "_" .. side) or role end

-- Human-readable name of a role key («Brazo der.»).
function R.label(key)
    local role, side = string.match(key, "^(%w+)_?([RL]?)$")
    for _, r in ipairs(R.ROLES) do
        if r.key == role then
            return r.label .. (side == "R" and " der." or side == "L" and " izq." or "")
        end
    end
    return key
end

-- Every role key in processing order (parents first).
function R.orderedKeys()
    local keys = {}
    for _, r in ipairs(R.ROLES) do
        if r.side then
            keys[#keys + 1] = r.key .. "_R"
            keys[#keys + 1] = r.key .. "_L"
        else
            keys[#keys + 1] = r.key
        end
    end
    return keys
end

return R
