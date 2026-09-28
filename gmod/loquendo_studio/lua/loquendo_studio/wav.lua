-- WAV reading and mouth-opening curve for lip sync (pure Lua).
-- Supports PCM 8/16/24/32-bit and float 32-bit, any channel count (what Loquendo TTS, SAPI and BALCON write).
local W = {}

local byte = string.byte

local function u16(s, i) local a, b = byte(s, i, i + 1) return a + b * 256 end
local function u32(s, i) local a, b, c, d = byte(s, i, i + 3) return a + b * 256 + c * 65536 + d * 16777216 end

-- Returns info = { rate, channels, bits, float, dataStart, dataLength, duration } or nil, error.
function W.parse(s)
    if not s or #s < 44 or s:sub(1, 4) ~= "RIFF" or s:sub(9, 12) ~= "WAVE" then
        return nil, "no es un archivo WAV"
    end
    local info, i = {}, 13
    while i + 8 <= #s do
        local id, size = s:sub(i, i + 3), u32(s, i + 4)
        local body = i + 8
        if id == "fmt " then
            local format = u16(s, body)
            info.channels = u16(s, body + 2)
            info.rate = u32(s, body + 4)
            info.bits = u16(s, body + 14)
            if format == 0xFFFE and size >= 26 then format = u16(s, body + 24) end -- WAVE_FORMAT_EXTENSIBLE
            info.float = format == 3
            if format ~= 1 and format ~= 3 then return nil, "formato WAV no soportado (" .. format .. ")" end
        elseif id == "data" then
            info.dataStart = body
            info.dataLength = math.min(size, #s - body + 1)
        end
        i = body + size + (size % 2)
    end
    if not info.rate or not info.dataStart then return nil, "WAV sin datos de audio" end
    info.frameBytes = info.channels * info.bits / 8
    info.samples = math.floor(info.dataLength / info.frameBytes)
    info.duration = info.samples / info.rate
    return info
end

local function sample(s, i, bits, float)
    if bits == 16 then
        local v = u16(s, i)
        if v >= 32768 then v = v - 65536 end
        return v / 32768
    elseif bits == 8 then
        return (byte(s, i) - 128) / 128
    elseif bits == 24 then
        local a, b, c = byte(s, i, i + 2)
        local v = a + b * 256 + c * 65536
        if v >= 8388608 then v = v - 16777216 end
        return v / 8388608
    elseif bits == 32 and float then
        local a, b, c, d = byte(s, i, i + 3)
        local sign = d >= 128 and -1 or 1
        local exp = (d % 128) * 2 + math.floor(c / 128)
        local mant = (c % 128) * 65536 + b * 256 + a
        if exp == 0 then return 0 end
        return sign * (1 + mant / 8388608) * 2 ^ (exp - 127)
    elseif bits == 32 then
        local v = u32(s, i)
        if v >= 2147483648 then v = v - 4294967296 end
        return v / 2147483648
    end
    return 0
end

-- Mouth opening per video frame (0..1).
-- opts.sens (0.2..3, default 1): gain; opts.gate (0..0.5, default 0.12): silence under this is a closed mouth;
-- opts.release (0..1, default 0.35): how fast the mouth closes (the opening is quick, like speech).
function W.mouth(s, info, fps, opts)
    opts = opts or {}
    local sens, gate, release = opts.sens or 1, opts.gate or 0.12, opts.release or 0.35
    local frames = math.max(1, math.ceil(info.duration * fps))
    local per = info.rate / fps
    local step = math.max(1, math.floor(per / 400)) -- ~400 samples per frame are plenty for an envelope
    local rms = {}
    for f = 1, frames do
        local first = math.floor((f - 1) * per)
        local last = math.min(info.samples - 1, math.floor(f * per) - 1)
        local sum, n = 0, 0
        for k = first, last, step do
            local base = info.dataStart + k * info.frameBytes
            local v = 0
            for c = 0, info.channels - 1 do
                v = v + sample(s, base + c * info.bits / 8, info.bits, info.float)
            end
            v = v / info.channels
            sum, n = sum + v * v, n + 1
        end
        rms[f] = n > 0 and math.sqrt(sum / n) or 0
    end

    -- Reference level: the 95th percentile, so one peak does not make everything else look quiet.
    local sorted = {}
    for i = 1, #rms do sorted[i] = rms[i] end
    table.sort(sorted)
    local ref = sorted[math.max(1, math.floor(#sorted * 0.95))]
    if ref < 1e-4 then ref = 1e-4 end

    local out, prev = {}, 0
    for f = 1, frames do
        local v = rms[f] / ref
        v = (v - gate) / (1 - gate)
        v = math.max(0, math.min(1, v)) ^ 0.7 * sens
        v = math.min(1, v)
        if v > prev then prev = prev + (v - prev) * 0.75 else prev = prev + (v - prev) * release end
        out[f] = prev < 0.03 and 0 or prev
    end
    return out
end

return W
