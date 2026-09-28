-- Loquendo Studio: pose designer, in-place animations, lip sync and transparent renders for Loquendo AI.
-- Client only; open it with the console command «loquendo_studio» or from the C menu.
local files = { "mat3", "rig", "acciones", "wav", "escena", "render", "ui" }
if SERVER then
    for _, f in ipairs(files) do AddCSLuaFile("loquendo_studio/" .. f .. ".lua") end
    return
end

LQS = LQS or {}
LQS.V = include("loquendo_studio/mat3.lua")
LQS.Rig = include("loquendo_studio/rig.lua")
LQS.Acc = include("loquendo_studio/acciones.lua")
LQS.Wav = include("loquendo_studio/wav.lua")
LQS.Escena = include("loquendo_studio/escena.lua")
LQS.Render = include("loquendo_studio/render.lua")
include("loquendo_studio/ui.lua")
