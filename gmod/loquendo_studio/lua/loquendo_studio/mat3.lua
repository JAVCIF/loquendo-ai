-- Pure Lua 3x3 rotation math (no GMod dependency, testable with plain LuaJIT).
-- Matrices are row-major {a11,a12,a13,a21,a22,a23,a31,a32,a33}; their COLUMNS are the local axes in world space,
-- the same convention as Source's bone matrices (forward, left, up). Vectors are {x, y, z}.
local M = {}

M.I = { 1, 0, 0, 0, 1, 0, 0, 0, 1 }

function M.dot(a, b) return a[1] * b[1] + a[2] * b[2] + a[3] * b[3] end

function M.cross(a, b)
    return { a[2] * b[3] - a[3] * b[2], a[3] * b[1] - a[1] * b[3], a[1] * b[2] - a[2] * b[1] }
end

function M.len(v) return math.sqrt(M.dot(v, v)) end

function M.norm(v)
    local l = M.len(v)
    if l < 1e-9 then return { 0, 0, 0 } end
    return { v[1] / l, v[2] / l, v[3] / l }
end

function M.add(a, b) return { a[1] + b[1], a[2] + b[2], a[3] + b[3] } end
function M.scale(v, s) return { v[1] * s, v[2] * s, v[3] * s } end

-- Linear mix of two directions, renormalized.
function M.mix(a, b, w) return M.norm({ a[1] + (b[1] - a[1]) * w, a[2] + (b[2] - a[2]) * w, a[3] + (b[3] - a[3]) * w }) end

function M.mul(a, b)
    local r = {}
    for i = 0, 2 do
        for j = 1, 3 do
            r[i * 3 + j] = a[i * 3 + 1] * b[j] + a[i * 3 + 2] * b[3 + j] + a[i * 3 + 3] * b[6 + j]
        end
    end
    return r
end

function M.t(a) return { a[1], a[4], a[7], a[2], a[5], a[8], a[3], a[6], a[9] } end

function M.vec(a, v)
    return {
        a[1] * v[1] + a[2] * v[2] + a[3] * v[3],
        a[4] * v[1] + a[5] * v[2] + a[6] * v[3],
        a[7] * v[1] + a[8] * v[2] + a[9] * v[3],
    }
end

-- Rotation of `deg` degrees around the unit axis (right-hand rule).
function M.axisAngle(axis, deg)
    local a = M.norm(axis)
    local r = math.rad(deg)
    local c, s = math.cos(r), math.sin(r)
    local t = 1 - c
    local x, y, z = a[1], a[2], a[3]
    return {
        t * x * x + c, t * x * y - s * z, t * x * z + s * y,
        t * x * y + s * z, t * y * y + c, t * y * z - s * x,
        t * x * z - s * y, t * y * z + s * x, t * z * z + c,
    }
end

-- Shortest-arc rotation taking direction d onto direction t.
function M.between(d, t)
    d, t = M.norm(d), M.norm(t)
    local c = M.dot(d, t)
    if c > 0.999999 then return M.I end
    if c < -0.999999 then
        local ortho = math.abs(d[1]) < 0.9 and { 1, 0, 0 } or { 0, 1, 0 }
        return M.axisAngle(M.cross(d, ortho), 180)
    end
    return M.axisAngle(M.cross(d, t), math.deg(math.acos(c)))
end

-- Rotates vector v around axis by deg.
function M.rotate(v, axis, deg) return M.vec(M.axisAngle(axis, deg), v) end

-- Removes scale/shear from a bone matrix (Gram-Schmidt on the columns).
function M.orthonormal(m)
    local f = M.norm({ m[1], m[4], m[7] })
    local l = { m[2], m[5], m[8] }
    l = M.norm(M.add(l, M.scale(f, -M.dot(l, f))))
    local u = M.cross(f, l)
    return { f[1], l[1], u[1], f[2], l[2], u[2], f[3], l[3], u[3] }
end

-- Angle between two rotations' effect on a direction (degrees); used by the self-calibration.
function M.angleBetween(a, b)
    local c = M.dot(M.norm(a), M.norm(b))
    return math.deg(math.acos(math.max(-1, math.min(1, c))))
end

return M
