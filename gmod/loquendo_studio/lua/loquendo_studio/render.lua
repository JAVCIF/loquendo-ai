-- Offline rendering to PNG files in garrysmod/data/loquendo_studio/renders/<name>/.
-- «Transparente» renders every frame twice, on black and on white: the difference gives an exact alpha,
-- including antialiased edges, hair cards and glass (see componer.ps1), with no chroma key.
local S = LQS.Escena
local R = { job = nil }

local function pow2(n)
    local p = 256
    while p < n do p = p * 2 end
    return p
end

local function target(size)
    return GetRenderTargetEx("loquendo_studio_rt_" .. size, size, size, RT_SIZE_LITERAL,
        MATERIAL_RT_DEPTH_SEPARATE, 256, 0, IMAGE_FORMAT_RGBA8888)
end

function R.Folder(name) return "loquendo_studio/renders/" .. name end

-- job = { nombre, ancho, alto, suavizado (bool), fps, frames, desde (s), modo = "transparente"|"color",
--         color = Color, estado = studio state copy, camara = camera copy, audio = file name or nil }
function R.Start(job, onProgress, onDone)
    if R.job then return false, "Ya hay un render en curso." end
    job.escala = job.suavizado and 2 or 1
    if math.max(job.ancho, job.alto) * job.escala > 4096 then job.escala = 1 end
    job.rw, job.rh = job.ancho * job.escala, job.alto * job.escala
    job.rt = target(pow2(math.max(job.rw, job.rh)))
    job.frame = 0
    job.onProgress, job.onDone = onProgress, onDone
    file.CreateDir(R.Folder(job.nombre))
    for _, f in ipairs(file.Find(R.Folder(job.nombre) .. "/*.png", "DATA")) do
        file.Delete(R.Folder(job.nombre) .. "/" .. f)
    end
    R.job = job
    return true
end

function R.Cancel()
    if R.job and R.job.onDone then R.job.onDone(false, "Render cancelado.") end
    R.job = nil
end

local function pass(job, name, r, g, b, a)
    render.PushRenderTarget(job.rt)
    render.Clear(r, g, b, a, true, true)
    render.SetWriteDepthToDestAlpha(false)
    local pos, ang = S.Camera(job.camara, job.ancho / job.alto)
    S.Draw(pos, ang, job.camara.fov, 0, 0, job.rw, job.rh)
    local data = render.Capture({ format = "png", x = 0, y = 0, w = job.rw, h = job.rh, alpha = false })
    render.SetWriteDepthToDestAlpha(true)
    render.PopRenderTarget()
    if data then
        file.Write(string.format("%s/%s_%05d.png", R.Folder(job.nombre), name, job.frame), data)
    end
    return data ~= nil
end

hook.Add("PostRender", "LoquendoStudioRender", function()
    local job = R.job
    if not job then return end
    if not IsValid(S.ent) then R.Cancel() return end
    job.frame = job.frame + 1
    local t = (job.desde or 0) + (job.frame - 1) / job.fps
    S.Pose(job.estado, t)
    local ok
    if job.modo == "transparente" then
        ok = pass(job, "negro", 0, 0, 0, 255) and pass(job, "blanco", 255, 255, 255, 255)
    else
        ok = pass(job, "color", job.color.r, job.color.g, job.color.b, 255)
    end
    if not ok then
        R.job = nil
        if job.onDone then job.onDone(false, "render.Capture no devolvió imagen.") end
        return
    end
    if job.onProgress then job.onProgress(job.frame / job.frames) end
    if job.frame >= job.frames then
        R.job = nil
        file.Write(R.Folder(job.nombre) .. "/render.json", util.TableToJSON({
            nombre = job.nombre, ancho = job.ancho, alto = job.alto, escala = job.escala, fps = job.fps,
            frames = job.frames, modo = job.modo, audio = job.audio, modelo = S.model,
        }, true))
        if job.onDone then job.onDone(true, job.frames) end
    end
end)

return R
