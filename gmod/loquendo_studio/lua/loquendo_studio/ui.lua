-- Studio window (Derma). Texts in Spanish; the data folder is garrysmod/data/loquendo_studio/.
local S, Rig, Acc, Wav, Render = LQS.Escena, LQS.Rig, LQS.Acc, LQS.Wav, LQS.Render

local AUDIO_DIR = "loquendo_studio/audio/"
local st = {
    modelo = "models/player/kleiner.mdl", skin = 0,
    accion = "respirar", usarPropia = true, secuencia = nil, vel = 1, amp = 1, dur = 4,
    pose = {}, flex = {}, mirar = true, poseT = 0,
    audio = nil, boca = nil, audioDur = nil, sens = 1, cierre = 0.35, fps = 30,
}
local camara = { yaw = 180, pitch = 4, zoom = 1, fov = 35, altura = 0, encuadre = "cuerpo" }
local salida = { nombre = "personaje", ancho = 1920, alto = 1080, fps = 30, suavizado = true, modo = "transparente",
    color = Color(0, 255, 0) }
local play = { on = false, t0 = 0 }
local frame

local function slider(parent, text, min, max, decimals, value, onChange)
    local s = vgui.Create("DNumSlider", parent)
    s:Dock(TOP)
    s:DockMargin(0, 0, 0, 2)
    s:SetText(text)
    s:SetMinMax(min, max)
    s:SetDecimals(decimals)
    s:SetValue(value)
    s:SetDark(true)
    s.OnValueChanged = function(_, v) onChange(v) end
    return s
end

local function label(parent, text, wrap)
    local l = vgui.Create("DLabel", parent)
    l:Dock(TOP)
    l:DockMargin(0, 4, 0, 2)
    l:SetDark(true)
    l:SetText(text)
    if wrap then l:SetWrap(true) l:SetAutoStretchVertical(true) end
    return l
end

local function button(parent, text, onClick)
    local b = vgui.Create("DButton", parent)
    b:Dock(TOP)
    b:DockMargin(0, 2, 0, 2)
    b:SetTall(26)
    b:SetText(text)
    b.DoClick = onClick
    return b
end

local function duration()
    return st.boca and st.audioDur or st.dur
end

local function stopSound()
    if IsValid(play.channel) then play.channel:Stop() end
    play.channel = nil
end

local function stop()
    play.on = false
    stopSound()
end

local function start(withSound)
    stop()
    play.on, play.t0 = true, RealTime()
    if withSound and st.audio then
        sound.PlayFile("data/" .. AUDIO_DIR .. st.audio, "noplay", function(ch)
            if IsValid(ch) and play.on then
                play.channel = ch
                ch:Play()
                play.t0 = RealTime()
            end
        end)
    end
end

local function currentTime()
    if not play.on then return st.poseT end
    local t = RealTime() - play.t0
    local d = duration()
    if t > d then
        if play.channel then stop() return st.poseT end
        play.t0 = RealTime()
        t = 0
    end
    return t
end

local function loadAudio(name)
    st.audio, st.boca, st.audioDur = nil, nil, nil
    if not name then return true end
    local fh = file.Open(AUDIO_DIR .. name, "rb", "DATA")
    if not fh then return false, "No se pudo abrir " .. name end
    local data = fh:Read(fh:Size())
    fh:Close()
    local info, err = Wav.parse(data)
    if not info then return false, name .. ": " .. err end
    st.audio, st.audioDur = name, info.duration + 0.25
    st.boca = Wav.mouth(data, info, st.fps, { sens = st.sens, release = st.cierre })
    st.audioData, st.audioInfo = data, info
    return true
end

local function recomputeMouth()
    if st.audioData then
        st.boca = Wav.mouth(st.audioData, st.audioInfo, st.fps, { sens = st.sens, release = st.cierre })
    end
end

-- ---------------------------------------------------------------- preview

local function buildPreview(parent)
    -- The toolbar is docked first so the FILL preview takes only the space left.
    local bar = vgui.Create("DPanel", parent)
    bar:Dock(BOTTOM)
    bar:SetTall(30)
    bar:DockPadding(4, 2, 4, 2)
    for _, e in ipairs({ { "Cuerpo entero", "cuerpo" }, { "Plano medio", "medio" }, { "Primer plano", "primer" } }) do
        local b = vgui.Create("DButton", bar)
        b:Dock(LEFT)
        b:SetWide(95)
        b:DockMargin(0, 0, 4, 0)
        b:SetText(e[1])
        b.DoClick = function() camara.encuadre, camara.zoom, camara.altura = e[2], 1, 0 end
    end
    for _, e in ipairs({ { "Frente", 180 }, { "3/4", 215 }, { "Perfil", 270 } }) do
        local b = vgui.Create("DButton", bar)
        b:Dock(LEFT)
        b:SetWide(60)
        b:DockMargin(0, 0, 4, 0)
        b:SetText(e[1])
        b.DoClick = function() camara.yaw, camara.pitch = e[2], 4 end
    end
    local hint = vgui.Create("DLabel", bar)
    hint:Dock(FILL)
    hint:SetText("  Arrastra: girar · Clic der.: subir/bajar · Rueda: zoom")
    local p = vgui.Create("DPanel", parent)
    p:Dock(FILL)
    p.Paint = function(self, w, h)
        surface.SetDrawColor(30, 30, 34)
        surface.DrawRect(0, 0, w, h)
        -- The output frame, letterboxed, so what you see is what gets rendered.
        local aspect = salida.ancho / salida.alto
        local fw, fh = w - 16, (w - 16) / aspect
        if fh > h - 16 then fh, fw = h - 16, (h - 16) * aspect end
        local fx, fy = math.floor((w - fw) / 2), math.floor((h - fh) / 2)
        if salida.modo == "transparente" then
            local cell = 12
            for y = 0, math.ceil(fh / cell) - 1 do
                for x = 0, math.ceil(fw / cell) - 1 do
                    local c = ((x + y) % 2 == 0) and 200 or 150
                    surface.SetDrawColor(c, c, c)
                    surface.DrawRect(fx + x * cell, fy + y * cell, math.min(cell, fw - x * cell), math.min(cell, fh - y * cell))
                end
            end
        else
            surface.SetDrawColor(salida.color.r, salida.color.g, salida.color.b)
            surface.DrawRect(fx, fy, fw, fh)
        end
        if not IsValid(S.ent) then
            draw.SimpleText("Carga un modelo en la pestaña «Modelo»", "DermaLarge", w / 2, h / 2, color_white, 1, 1)
            return
        end
        if Render.job then
            draw.SimpleText("Renderizando…", "DermaLarge", w / 2, h / 2, color_white, 1, 1)
            return
        end
        local t = currentTime()
        S.Pose(st, t)
        local sx, sy = self:LocalToScreen(fx, fy)
        local pos, ang = S.Camera(camara, aspect)
        S.Draw(pos, ang, camara.fov, sx, sy, fw, fh)
        draw.SimpleText(string.format("%.2f s", t), "DermaDefault", fx + 6, fy + 4, color_black)
    end
    p.OnMousePressed = function(self, code)
        self.drag = { x = gui.MouseX(), y = gui.MouseY(), yaw = camara.yaw, pitch = camara.pitch, alt = camara.altura, code = code }
        self:MouseCapture(true)
    end
    p.OnMouseReleased = function(self)
        self.drag = nil
        self:MouseCapture(false)
    end
    p.Think = function(self)
        local d = self.drag
        if not d then return end
        local dx, dy = gui.MouseX() - d.x, gui.MouseY() - d.y
        if d.code == MOUSE_RIGHT then
            camara.altura = d.alt + dy * 0.002
        else
            camara.yaw = d.yaw - dx * 0.4
            camara.pitch = math.Clamp(d.pitch + dy * 0.3, -80, 80)
        end
    end
    p.OnMouseWheeled = function(_, delta)
        camara.zoom = math.Clamp(camara.zoom * (1 - delta * 0.08), 0.15, 5)
    end

    return p
end

-- ---------------------------------------------------------------- tabs

local refreshModelTabs = {}

local function tabModelo(sheet)
    local p = vgui.Create("DPanel", sheet)
    p:DockPadding(8, 8, 8, 8)
    label(p, "Modelo (ruta .mdl, por ejemplo models/player/kleiner.mdl):")
    local entry = vgui.Create("DTextEntry", p)
    entry:Dock(TOP)
    entry:SetValue(st.modelo)
    local report = vgui.Create("DTextEntry", p)
    local skinSlider
    local function load(model)
        stop()
        st.modelo = model
        st.pose, st.flex = {}, {}
        local ok, text = S.Load(model)
        report:SetValue(text)
        if ok then
            skinSlider:SetMinMax(0, math.max(0, S.ent:SkinCount() - 1))
            for _, f in ipairs(refreshModelTabs) do f() end
        end
    end
    button(p, "Cargar y analizar", function() load(string.Trim(entry:GetValue())) end)
    button(p, "Usar mi modelo de jugador", function()
        entry:SetValue(LocalPlayer():GetModel())
        load(LocalPlayer():GetModel())
    end)
    skinSlider = slider(p, "Skin", 0, 0, 0, 0, function(v) st.skin = math.Round(v) end)
    label(p, "Análisis del modelo:")
    report:Dock(FILL)
    report:SetMultiline(true)
    report:SetEditable(false)
    report:SetFont("DermaDefault")
    timer.Simple(0, function() if IsValid(report) then load(st.modelo) end end)
    return p
end

local function tabAnimacion(sheet)
    local p = vgui.Create("DPanel", sheet)
    p:DockPadding(8, 8, 8, 8)
    local list = vgui.Create("DListView", p)
    list:Dock(TOP)
    list:SetTall(250)
    list:SetMultiSelect(false)
    list:AddColumn("Acción")
    list:AddColumn("Tipo"):SetFixedWidth(70)
    for _, a in ipairs(Acc.LIST) do
        local line = list:AddLine(a.nombre, a.bucle and "bucle" or "gesto")
        line.accion = a
        if a.id == st.accion then list:SelectItem(line) end
    end
    local durSlider
    list.OnRowSelected = function(_, _, line)
        st.accion, st.dur = line.accion.id, line.accion.dur
        if durSlider then durSlider:SetValue(st.dur) end
        start(false)
    end

    local own = vgui.Create("DCheckBoxLabel", p)
    own:Dock(TOP)
    own:DockMargin(0, 6, 0, 4)
    own:SetDark(true)
    own:SetText("Usar la animación propia del modelo cuando la tenga (modelos de jugador)")
    own:SetValue(st.usarPropia)
    own.OnChange = function(_, v) st.usarPropia = v end

    label(p, "Secuencia del modelo (opcional, sustituye a la acción):")
    local seq = vgui.Create("DComboBox", p)
    seq:Dock(TOP)
    seq.OnSelect = function(_, _, _, data)
        st.secuencia = data >= 0 and data or nil
        if st.secuencia then st.usarPropia = true own:SetValue(true) end
    end
    table.insert(refreshModelTabs, function()
        seq:Clear()
        st.secuencia = nil
        seq:AddChoice("(automática según la acción)", -1, true)
        for id, name in pairs(S.ent:GetSequenceList() or {}) do seq:AddChoice(name, id) end
    end)

    slider(p, "Velocidad", 0.25, 3, 2, st.vel, function(v) st.vel = v end)
    slider(p, "Intensidad", 0, 2, 2, st.amp, function(v) st.amp = v end)
    durSlider = slider(p, "Duración (s)", 0.5, 30, 1, st.dur, function(v) st.dur = v end)
    button(p, "▶ Reproducir", function() start(false) end)
    button(p, "■ Parar (vuelve a la pose)", stop)
    return p
end

local function tabPose(sheet)
    local p = vgui.Create("DPanel", sheet)
    p:DockPadding(8, 8, 8, 8)
    label(p, "La pose es la acción elegida congelada en un instante, más tus ajustes.", true)
    slider(p, "Congelar en el segundo", 0, 10, 2, st.poseT, function(v) st.poseT = v stop() end)

    local mirar = vgui.Create("DCheckBoxLabel", p)
    mirar:Dock(TOP)
    mirar:DockMargin(0, 4, 0, 4)
    mirar:SetDark(true)
    mirar:SetText("Los ojos miran a la cámara")
    mirar:SetValue(st.mirar)
    mirar.OnChange = function(_, v) st.mirar = v end

    label(p, "Articulación:")
    local role = vgui.Create("DComboBox", p)
    role:Dock(TOP)
    local sliders = {}
    local current
    local function values() return st.pose[current] or { 0, 0, 0 } end
    local function set(i, v)
        if not current then return end
        local vals = values()
        vals[i] = v
        st.pose[current] = vals
        stop()
    end
    sliders[1] = slider(p, "Adelante / atrás", -120, 120, 0, 0, function(v) set(1, v) end)
    sliders[2] = slider(p, "Abrir / cerrar (lado)", -120, 120, 0, 0, function(v) set(2, v) end)
    sliders[3] = slider(p, "Girar", -120, 120, 0, 0, function(v) set(3, v) end)
    role.OnSelect = function(_, _, _, key)
        current = key
        local vals = values()
        for i = 1, 3 do sliders[i]:SetValue(vals[i]) end
    end
    button(p, "Restablecer esta articulación", function()
        if current then st.pose[current] = nil role.OnSelect(role, 0, "", current) end
    end)
    button(p, "Restablecer toda la pose", function()
        st.pose = {}
        if current then role.OnSelect(role, 0, "", current) end
    end)

    label(p, "Expresión (flexes del modelo):")
    local scroll = vgui.Create("DScrollPanel", p)
    scroll:Dock(FILL)
    table.insert(refreshModelTabs, function()
        role:Clear()
        current = nil
        for _, key in ipairs(Rig.orderedKeys()) do
            if S.rig[key] then role:AddChoice(Rig.label(key), key) end
        end
        scroll:Clear()
        local count = 0
        for i, name in SortedPairs(S.flexNames) do
            local lo, hi = S.ent:GetFlexBounds(i)
            local s = vgui.Create("DNumSlider", scroll)
            s:Dock(TOP)
            s:SetText(name)
            s:SetMinMax(lo, hi)
            s:SetDecimals(2)
            s:SetValue(0)
            s:SetDark(true)
            s.OnValueChanged = function(_, v) st.flex[i] = v end
            count = count + 1
        end
        if count == 0 then
            local l = vgui.Create("DLabel", scroll)
            l:Dock(TOP)
            l:SetDark(true)
            l:SetText("Este modelo no tiene expresiones faciales.")
        end
    end)
    return p
end

local function tabVoz(sheet)
    local p = vgui.Create("DPanel", sheet)
    p:DockPadding(8, 8, 8, 8)
    label(p, "Pon los .wav en garrysmod/data/loquendo_studio/audio/ (las voces que genera Loquendo AI sirven tal cual). La boca se abre según el volumen de la voz.", true)
    local combo = vgui.Create("DComboBox", p)
    combo:Dock(TOP)
    local status = label(p, "Sin audio: la animación dura lo que indique «Duración».", true)
    local function refresh()
        combo:Clear()
        combo:AddChoice("(sin audio)", false, st.audio == nil)
        for _, f in ipairs(file.Find(AUDIO_DIR .. "*.wav", "DATA")) do combo:AddChoice(f, f, f == st.audio) end
    end
    combo.OnSelect = function(_, _, _, data)
        stop()
        local ok, err = loadAudio(data or nil)
        if not ok then status:SetText(err)
        elseif st.audio then status:SetText(string.format("%s · %.1f s · la animación dura lo mismo que la voz.", st.audio, st.audioDur))
        else status:SetText("Sin audio: la animación dura lo que indique «Duración».") end
    end
    button(p, "Actualizar lista", refresh)
    slider(p, "Sensibilidad (apertura)", 0.2, 3, 2, st.sens, function(v) st.sens = v recomputeMouth() end)
    slider(p, "Cierre de la boca (rapidez)", 0.05, 1, 2, st.cierre, function(v) st.cierre = v recomputeMouth() end)
    button(p, "▶ Probar con la voz", function()
        if not st.audio then status:SetText("Elige primero un .wav.") return end
        start(true)
    end)
    button(p, "■ Parar", stop)
    label(p, "Consejo: combina la voz con la acción «Hablar (gesticular)» para que el personaje mueva también los brazos y la cabeza.", true)
    file.CreateDir("loquendo_studio/audio")
    refresh()
    return p
end

local function tabRender(sheet)
    local p = vgui.Create("DPanel", sheet)
    p:DockPadding(8, 8, 8, 8)
    label(p, "Nombre:")
    local name = vgui.Create("DTextEntry", p)
    name:Dock(TOP)
    name:SetValue(salida.nombre)

    label(p, "Tamaño:")
    local size = vgui.Create("DComboBox", p)
    size:Dock(TOP)
    for _, s in ipairs({ { 1920, 1080 }, { 1280, 720 }, { 1080, 1920 }, { 1080, 1080 }, { 1024, 1024 }, { 512, 512 } }) do
        size:AddChoice(s[1] .. " × " .. s[2], s, s[1] == salida.ancho and s[2] == salida.alto)
    end
    size.OnSelect = function(_, _, _, s) salida.ancho, salida.alto = s[1], s[2] end

    label(p, "Fotogramas por segundo:")
    local fps = vgui.Create("DComboBox", p)
    fps:Dock(TOP)
    for _, f in ipairs({ 24, 25, 30, 60 }) do fps:AddChoice(tostring(f), f, f == salida.fps) end
    fps.OnSelect = function(_, _, _, f) salida.fps, st.fps = f, f recomputeMouth() end

    local smooth = vgui.Create("DCheckBoxLabel", p)
    smooth:Dock(TOP)
    smooth:DockMargin(0, 6, 0, 2)
    smooth:SetDark(true)
    smooth:SetText("Bordes suaves (renderiza al doble y reduce; más lento)")
    smooth:SetValue(salida.suavizado)
    smooth.OnChange = function(_, v) salida.suavizado = v end

    label(p, "Fondo:")
    local mode = vgui.Create("DComboBox", p)
    mode:Dock(TOP)
    mode:AddChoice("Transparente (canal alfa real, sin chroma)", "transparente", salida.modo == "transparente")
    mode:AddChoice("Color sólido (chroma o fondo plano)", "color", salida.modo == "color")
    local mixer = vgui.Create("DColorMixer", p)
    mixer:Dock(TOP)
    mixer:SetTall(90)
    mixer:SetPalette(false)
    mixer:SetAlphaBar(false)
    mixer:SetColor(salida.color)
    mixer:SetVisible(salida.modo == "color")
    mixer.ValueChanged = function(_, c) salida.color = Color(c.r, c.g, c.b) end
    mode.OnSelect = function(_, _, _, m)
        salida.modo = m
        mixer:SetVisible(m == "color")
        p:InvalidateLayout()
    end

    local bar = vgui.Create("DProgress", p)
    local status
    local function go(single)
        if not IsValid(S.ent) then status:SetText("Carga primero un modelo.") return end
        stop()
        salida.nombre = string.gsub(string.lower(string.Trim(name:GetValue())), "[^%w_%-]", "_")
        if salida.nombre == "" then salida.nombre = "personaje" end
        local frames = single and 1 or math.max(1, math.ceil(duration() * salida.fps))
        local ok, err = Render.Start({
            nombre = salida.nombre, ancho = salida.ancho, alto = salida.alto, suavizado = salida.suavizado,
            fps = salida.fps, frames = frames, desde = single and st.poseT or 0, modo = salida.modo,
            color = salida.color, estado = table.Copy(st), camara = table.Copy(camara),
            audio = (not single) and st.audio or nil,
        }, function(f) if IsValid(bar) then bar:SetFraction(f) end end,
        function(done, info)
            if not IsValid(status) then return end
            if done then
                status:SetText(string.format("Listo: %d fotograma(s) en garrysmod/data/%s/.\nEjecuta componer.ps1 con esa carpeta para obtener el %s con transparencia.",
                    info, Render.Folder(salida.nombre), single and ".png" or ".mov"))
            else
                status:SetText(info)
            end
        end)
        if not ok then status:SetText(err) else status:SetText(string.format("Renderizando %d fotograma(s)…", frames)) end
    end
    button(p, "Imagen de la pose actual (PNG)", function() go(true) end)
    button(p, "Animación completa (secuencia → .mov)", function() go(false) end)
    button(p, "Cancelar render", function() Render.Cancel() end)
    bar:Dock(TOP)
    bar:DockMargin(0, 6, 0, 4)
    bar:SetTall(16)
    bar:SetFraction(0)
    status = label(p, "", true)
    return p
end

-- ---------------------------------------------------------------- window

function LQS.Abrir()
    if IsValid(frame) then frame:MakePopup() return end
    refreshModelTabs = {}
    frame = vgui.Create("DFrame")
    frame:SetTitle("Loquendo Studio · poses, animaciones y lipsync")
    frame:SetSize(math.min(ScrW() - 40, 1280), math.min(ScrH() - 40, 780))
    frame:Center()
    frame:SetSizable(true)
    frame:MakePopup()
    frame.OnClose = function()
        stop()
        Render.Cancel()
    end

    local right = vgui.Create("DPropertySheet", frame)
    right:Dock(RIGHT)
    right:SetWide(430)
    right:AddSheet("Modelo", tabModelo(right), "icon16/user.png")
    right:AddSheet("Animación", tabAnimacion(right), "icon16/film.png")
    right:AddSheet("Pose", tabPose(right), "icon16/user_edit.png")
    right:AddSheet("Voz", tabVoz(right), "icon16/sound.png")
    right:AddSheet("Render", tabRender(right), "icon16/picture_save.png")

    local left = vgui.Create("DPanel", frame)
    left:Dock(FILL)
    left:DockMargin(0, 0, 6, 0)
    buildPreview(left)
end

concommand.Add("loquendo_studio", function() LQS.Abrir() end)

list.Set("DesktopWindows", "LoquendoStudio", {
    title = "Loquendo Studio",
    icon = "icon64/playermodel.png",
    init = function(_, window)
        if IsValid(window) then window:Remove() end
        LQS.Abrir()
    end,
})
