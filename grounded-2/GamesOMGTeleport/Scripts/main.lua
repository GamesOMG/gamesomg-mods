-- GamesOMG Teleport for Grounded 2 · UE4SS 4.0.0 Lua mod · MIT · https://gamesomg.com/grounded-2-map/
-- Pick a place. Go there. Get back.
--   F7  opens the panel: paste a /tp line copied from a map pin (Ctrl+V, Enter), or type to find a place and press Enter.
--   F8  takes you back to where you stood before your last teleport (press again to swap back).
-- Moves you with the game's own fast-travel call (SurvivalPlayerCharacter:TeleportToNocheck, the one the PTS uses), the
-- engine's K2_TeleportTo as a fallback. Watches the landing: a spot that doesn't hold (you keep falling, or the area never
-- loads) puts you back where you were. Keys live in settings.txt next to this mod's folder name.

local VERSION = "0.2.1"
local TAG = "[GamesOMG Teleport] "
local UEHelpers = require("UEHelpers")

local function log(s) print(TAG .. tostring(s) .. "\n") end

-- ── where this mod lives (settings.txt sits in the mod folder, one up from Scripts) ──────────────────────────────────
local SCRIPTS = (debug.getinfo(1, "S").source or ""):match("^@(.*[\\/])") or ""
local MODDIR = SCRIPTS:gsub("[Ss]cripts[\\/]$", "")

local SET = { PanelKey = "F7", BackKey = "F8" }
do
    local f = io.open(MODDIR .. "settings.txt", "r")
    if f then
        for line in f:lines() do
            local k, v = line:match("^%s*([%w_]+)%s*=%s*([^;#]-)%s*$")
            if k and v and v ~= "" then SET[k] = v end
        end
        f:close()
    end
end

local okPlaces, PLACES = pcall(require, "places")
if not okPlaces or type(PLACES) ~= "table" then
    log("places.lua missing or broken: the panel has no list (" .. tostring(PLACES) .. ")")
    PLACES = { places = {}, groups = {} }
end

-- ── colours: the GamesOMG panel palette (the Planet Crafter mod's), given in sRGB and turned linear for UMG ─────────
local function lin(hex, a)
    local function ch(v)
        v = v / 255
        return v <= 0.04045 and v / 12.92 or ((v + 0.055) / 1.055) ^ 2.4
    end
    return { R = ch((hex >> 16) & 255), G = ch((hex >> 8) & 255), B = ch(hex & 255), A = a or 1.0 }
end
local COL = {
    panel = lin(0x0E1015, 0.86), field = lin(0x21252D), line = lin(0x2B3038),
    text = lin(0xEBF0F5), muted = lin(0x9EABBA), dim = lin(0x6B7685),
    accent = lin(0xFF9E33), orange = lin(0xF08000), bad = lin(0xFF8A80), good = lin(0x8CD99A),
    hover = lin(0xFFFFFF, 0.07), sel = lin(0xFF9E33, 0.22), clear = lin(0x000000, 0.0),
    btn = lin(0xFF9E33, 0.18), btnHover = lin(0xFF9E33, 0.32),
}

-- ── state ───────────────────────────────────────────────────────────────────────────────────────────────────────────
local S = {
    ui = nil, toast = nil, open = false, back = nil, pending = nil,
    rows = {}, heads = {}, vis = {}, sel = 0, lastText = nil, hovered = nil,
    toastLeft = 0, prevCursor = false, tick = 0, uiFailed = false,
}

-- ── engine helpers ──────────────────────────────────────────────────────────────────────────────────────────────────
local CLS = {}
local function cls(path)
    if CLS[path] == nil then
        local c = StaticFindObject(path)
        CLS[path] = (c and c:IsValid()) and c or false
    end
    return CLS[path] or nil
end

local function wbl() return cls("/Script/UMG.Default__WidgetBlueprintLibrary") end

local function pawnOf()
    local pc = UEHelpers.GetPlayerController()
    if not (pc and pc:IsValid()) then return nil, nil end
    local p = pc.Pawn
    if p and p:IsValid() then return pc, p end
    return pc, nil
end

local function isPlayerChar(p)
    local c = cls("/Script/Maine.SurvivalPlayerCharacter")
    if not c then return true end           -- can't check: let the call decide
    local ok, r = pcall(function() return p:IsA(c) end)
    return (not ok) or r
end

local function movementMode(p)
    local ok, m = pcall(function() return p.CharacterMovement.MovementMode end)
    return ok and m or -1
end

local function v3(x, y, z) return { X = x + 0.0, Y = y + 0.0, Z = z + 0.0 } end

-- a widget we made can be garbage-collected by the engine (a panel taken off screen, a world reload): check before use
local function alive(o)
    local ok, v = pcall(function() return o ~= nil and o:IsValid() end)
    return ok and v == true
end

-- ── widgets, built from Lua (no .pak): a native CommonUserWidget hosts a tree we make ourselves ─────────────────────
local function new(path, outer)
    local c = cls(path)
    if not c then error("no class " .. path) end
    local o = StaticConstructObject(c, outer)
    if not (o and o:IsValid()) then error("could not make " .. path) end
    return o
end

local HOSTS = { "/Script/CommonUI.CommonUserWidget", "/Script/UMG.UserWidget" }
local function makeHost(pc)
    for _, path in ipairs(HOSTS) do
        local c = cls(path)
        if c then
            local ok, w = pcall(function() return wbl():Create(pc, c, pc) end)
            if ok and w and w:IsValid() then
                if not (w.WidgetTree and w.WidgetTree:IsValid()) then
                    w.WidgetTree = new("/Script/UMG.WidgetTree", w)
                end
                return w
            end
        end
    end
    error("no widget class this game lets us create")
end

local FONT = nil   -- the font the game's own UI text uses, found once
local function findFont()
    if FONT ~= nil then return FONT or nil end
    FONT = false
    local want = SET.Font
    local all = FindAllOf("Font") or {}
    local best, score = nil, -1
    for _, f in ipairs(all) do
        local n = f:GetFullName()
        local s = 0
        if want and n:find(want, 1, true) then s = 100
        elseif n:find("/Engine/EngineFonts/Roboto.Roboto", 1, true) then s = 50   -- Regular + Bold faces, every build
        elseif n:find("Roboto", 1, true) and not n:find("DistanceField", 1, true) then s = 10
        elseif n:lower():find("body", 1, true) or n:lower():find("regular", 1, true) then s = 20 end
        if n:find("/Engine/", 1, true) and s < 10 then s = s + 1 end
        if s > score then best, score = f, s end
    end
    if best then FONT = best; log("font: " .. best:GetFullName()) end
    return FONT or nil
end

local function styleText(tb, size, colour, bold)
    pcall(function()
        local f = tb.Font
        local font = findFont()
        if font then f.FontObject = font end
        f.Size = size
        if bold ~= nil then f.TypefaceFontName = FName(bold and "Bold" or "Regular") end
        tb:SetFont(f)
    end)
    pcall(function() tb:SetColorAndOpacity({ SpecifiedColor = colour, ColorUseRule = 0 }) end)
end

local function text(tree, s, size, colour, bold)
    local tb = new("/Script/UMG.TextBlock", tree)
    tb:SetText(FText(s))
    styleText(tb, size, colour, bold)
    pcall(function() tb:SetVisibility(3) end)    -- HitTestInvisible: the row under it takes the mouse
    return tb
end

-- long text wraps inside its own column and is clipped to it, so it never runs over the text beside it (0.2.0 drew long
-- names over the PTS / N · E / UNDER WATER labels). The clip covers the first frame, before the wrap width is known.
local function wrapped(tb)
    pcall(function() tb:SetAutoWrapText(true) end)
    pcall(function() tb.AutoWrapText = true end)
    pcall(function() tb:SetClipping(1) end)        -- EWidgetClipping::ClipToBounds
    return tb
end

local function setText(tb, s, colour)
    tb:SetText(FText(s))
    if colour then pcall(function() tb:SetColorAndOpacity({ SpecifiedColor = colour, ColorUseRule = 0 }) end) end
end

local function rounded(border, r)
    pcall(function()
        local b = border.Background
        b.DrawAs = 4                                   -- RoundedBox
        b.OutlineSettings.CornerRadii = { X = r, Y = r, Z = r, W = r }
        b.OutlineSettings.RoundingType = 0             -- FixedRadius
        border:SetBrush(b)
    end)
end

local function box(tree, colour, pad, radius)
    local b = new("/Script/UMG.Border", tree)
    b:SetBrushColor(colour)
    b:SetPadding(pad)
    if radius then rounded(b, radius) end
    return b
end

local function M(l, t, r, b) return { Left = l, Top = t, Right = r or l, Bottom = b or t } end

local function vadd(vb, w, pad, fill)
    local s = vb:AddChildToVerticalBox(w)
    if pad then s:SetPadding(pad) end
    if fill then s:SetSize({ SizeRule = 1, Value = 1.0 }) end
    return s
end

local function hadd(hb, w, pad, fill)
    local s = hb:AddChildToHorizontalBox(w)
    if pad then s:SetPadding(pad) end
    if fill then s:SetSize({ SizeRule = 1, Value = 1.0 }) end
    s:SetVerticalAlignment(2)
    return s
end

local function centre(canvas, w, sx, sy, ay, py)
    local s = canvas:AddChildToCanvas(w)
    s:SetAnchors({ Minimum = { X = 0.5, Y = ay or 0.5 }, Maximum = { X = 0.5, Y = ay or 0.5 } })
    s:SetAlignment({ X = 0.5, Y = ay or 0.5 })
    if sx then s:SetSize({ X = sx, Y = sy }) else s:SetAutoSize(true) end
    s:SetPosition({ X = 0, Y = py or 0 })
    return s
end

-- ── my places + ticks: plain text files next to the mod (My places hold across your worlds: one map; ticks don't) ─────
local MINE_FILE, TICK_FILE = MODDIR .. "my-places.txt", MODDIR .. "ticks.txt"
S.saved, S.ticks, S.sections, S.items = {}, {}, nil, {}
do
    local f = io.open(MINE_FILE, "r")
    if f then
        for line in f:lines() do
            local n, x, y, z, yaw = line:match("^(.-)\t(%-?[%d.]+)\t(%-?[%d.]+)\t(%-?[%d.]+)\t(%-?[%d.]+)%s*$")
            if n then S.saved[#S.saved + 1] = { n = n, x = tonumber(x), y = tonumber(y), z = tonumber(z), yaw = tonumber(yaw) } end
        end
        f:close()
    end
end

-- ticks are kept per world (two worlds share the yard, not their finds), keyed by the game's own id for the loaded world:
-- the game state's PlaythroughGuid, the same id the world's save folders carry ("(ID-<32 hex>)…")
local function readTicks(path)
    local out = {}
    local t = io.open(path, "r")
    if t then
        for line in t:lines() do
            local id = line:match("^%s*(.-)%s*$")
            if id ~= "" then out[id] = true end
        end
        t:close()
    end
    return out
end

local function writeLines(path, lines)
    local f = io.open(path, "w")
    if not f then log("can't write " .. path); return false end
    for _, l in ipairs(lines) do f:write(l, "\n") end
    f:close()
    return true
end

local function saveMine()
    local L = {}
    for _, p in ipairs(S.saved) do
        L[#L + 1] = string.format("%s\t%.0f\t%.0f\t%.0f\t%.0f", (p.n:gsub("[\t\r\n]", " ")), p.x, p.y, p.z, p.yaw or 0)
    end
    return writeLines(MINE_FILE, L)
end

local function saveTicks()
    local L = {}
    for id in pairs(S.ticks) do L[#L + 1] = id end
    table.sort(L)
    return writeLines(S.tickFile or TICK_FILE, L)
end

-- 0.2.0 read SaveLoadManager.CurrentPlaythroughSharedSaveID: that is a Shared World's cloud id, empty for a world on your
-- own PC, so every world fell back to one list. The game state holds the world's id whichever kind it is.
local function guidHex(g)
    local out = {}
    for _, f in ipairs({ "A", "B", "C", "D" }) do
        local v = g[f]
        if type(v) ~= "number" then return nil end
        out[#out + 1] = string.format("%08X", math.floor(v) & 0xFFFFFFFF)
    end
    local s = table.concat(out)
    if s:match("^0+$") then return nil end         -- no world yet (main menu), or a guest the host didn't tell
    return s
end

local function worldKey()
    local gs = UEHelpers.GetGameStateBase()
    if not alive(gs) then return nil end
    local id, name
    pcall(function() id = guidHex(gs.PlaythroughGuid) end)
    pcall(function() name = gs.PlaythroughName:ToString() end)
    if not id then return nil, nil, name end
    return id, "PlaythroughGuid", name
end

local function countTicks() local n = 0; for _ in pairs(S.ticks) do n = n + 1 end; return n end

-- the loaded world's ticks; the old one-file ticks carry into whichever world loads first. True when it changed
local function useWorld()
    local key, via, wname = worldKey()
    key = key or "all"
    if key == S.worldKey then return false end
    S.worldKey = key
    S.tickFile = (key == "all") and TICK_FILE or (MODDIR .. "ticks-" .. key .. ".txt")
    S.ticks = readTicks(S.tickFile)
    if key ~= "all" and next(S.ticks) == nil then
        local legacy = readTicks(TICK_FILE)
        if next(legacy) then
            S.ticks = legacy
            if saveTicks() then os.rename(TICK_FILE, MODDIR .. "ticks-carried-into-" .. key .. ".txt") end
            log("the old ticks file was carried into world " .. key)
        end
    end
    log("world " .. key .. ((wname and wname ~= "") and (" \"" .. wname .. "\"") or "")
        .. (via and (" (read via " .. via .. ")") or " (the game didn't say which world: one list for all)") .. ": " .. countTicks() .. " found")
    if S.sections then S.sections = nil end
    return true
end

-- the game's own map coordinates (the GamesOMG map's fit, 2026-10-20); the Abyss prints none
local function neOf(x, y)
    if x > 150000 and y > 80000 then return "The Abyss" end
    return string.format("%d N · %d E", math.floor(-5.971359e-4 * x - 55.9781 + 0.5), math.floor(-6.007775e-4 * y + 18.2186 + 0.5))
end

local function whereText(p)
    if p.ne then return string.format("%d N · %d E", p.ne[1], p.ne[2]) end
    if p.where and p.where ~= "" then return p.where end
    return neOf(p.x, p.y)
end

local function placeKey(p) return p.id or (p.n .. "@" .. math.floor(p.x) .. "," .. math.floor(p.y)) end

-- sections: My places first (open), then the map's groups, closed until you open one or type
local function sections()
    if S.sections then return S.sections end
    local secs = { { id = "mine", label = "My places", open = true, mine = true, items = S.saved } }
    local byG = {}
    for _, g in ipairs(PLACES.groups or {}) do
        local s = { id = g.id, label = g.label, open = false, items = {} }
        secs[#secs + 1] = s
        byG[g.id] = s
    end
    for _, p in ipairs(PLACES.places or {}) do
        local s = byG[p.g]
        if not s then
            s = { id = p.g, label = p.g, open = false, items = {} }
            secs[#secs + 1] = s
            byG[p.g] = s
        end
        table.insert(s.items, p)
    end
    for i, s in ipairs(secs) do
        if i > 1 then table.sort(s.items, function(a, b) return a.n:lower() < b.n:lower() end) end
    end
    -- Found: a ticked place leaves its group and sits in a closed drawer at the bottom, under the same
    -- heading, in the same order; unticked, it goes back
    S.drawer = { id = "found", label = "Found", open = false, drawer = true }
    for i, s in ipairs(secs) do
        if i > 1 then s.found = { id = s.id .. "-found", label = s.label, open = true, parent = S.drawer, twin = s } end
    end
    S.sections = secs
    return secs
end

local function sized(tree, w, width, height)
    local sb = new("/Script/UMG.SizeBox", tree)
    if width then sb:SetWidthOverride(width) end
    if height then sb:SetHeightOverride(height) end
    sb:SetContent(w)
    return sb
end

-- (unused since the panel standard: found places move, never struck) kept for a future "done" style
local function strike(tb, on)
    pcall(function()
        local b = tb.StrikeBrush
        b.DrawAs = 3
        b.ImageSize = { X = 1.0, Y = 2.0 }
        b.TintColor = { SpecifiedColor = on and COL.muted or COL.clear, ColorUseRule = 0 }
        tb:SetStrikeBrush(b)
    end)
end

local function headRow(tree, list, s)
    local row = box(tree, COL.clear, M(8, 9, 10, 6), 6)
    pcall(function() row:SetVisibility(0) end)
    local hb = new("/Script/UMG.HorizontalBox", tree)
    row:SetContent(hb)
    local arrow = text(tree, "+", 14, COL.accent, true)   -- + closed, – open: Roboto has no ▸ ▾ (0.2.0 showed a box)
    hadd(hb, sized(tree, arrow, 18), nil)
    hadd(hb, text(tree, string.upper(s.label), 13, COL.accent, true), M(4, 0, 0, 0), true)
    local count = text(tree, "", 12, COL.muted)
    hadd(hb, count, M(8, 0, 0, 0))
    list:AddChild(row)
    return { w = row, head = true, sec = s, arrow = arrow, count = count }
end

local function hintRow(tree, list, s, msg)
    local row = box(tree, COL.clear, M(14, 2, 10, 8), 6)
    row:SetContent(wrapped(text(tree, msg, 13, COL.dim)))
    list:AddChild(row)
    return { w = row, hint = true, sec = s }
end

local function placeRow(tree, list, s, p)
    local row = box(tree, COL.clear, M(10, 6, 10, 6), 6)
    pcall(function() row:SetVisibility(0) end)
    local hb = new("/Script/UMG.HorizontalBox", tree)
    row:SetContent(hb)
    local r = { w = row, p = p, sec = s, key = (p.n .. " " .. (p.s or "") .. " " .. (p.a or "")):lower() }
    if s.mine then
        hadd(hb, sized(tree, text(tree, "●", 12, COL.orange), 22), M(0, 0, 8, 0))
    else
        local tb = box(tree, COL.field, M(0, 0), 4)
        local mark = text(tree, "", 15, COL.accent, true)
        pcall(function() tb:SetHorizontalAlignment(2); tb:SetVerticalAlignment(2) end)
        tb:SetContent(mark)
        pcall(function() tb:SetVisibility(0) end)
        hadd(hb, sized(tree, tb, 22, 22), M(0, 0, 8, 0))
        r.tick, r.mark = tb, mark
    end
    r.name = wrapped(text(tree, p.n, 16, COL.text))
    hadd(hb, r.name, nil, true)                    -- the name takes what the labels leave, and wraps in it
    if p.water then hadd(hb, text(tree, "UNDER WATER", 11, COL.bad, true), M(10, 0, 0, 0)) end   -- suit up
    hadd(hb, text(tree, (p.s and (p.s .. "   ") or "") .. whereText(p), 12, COL.muted), M(10, 0, 0, 0))
    if s.mine then
        local del = box(tree, COL.clear, M(7, 0), 4)
        del:SetContent(text(tree, "×", 17, COL.dim, true))
        pcall(function() del:SetVisibility(0) end)
        hadd(hb, del, M(6, 0, 0, 0))
        r.del = del
    end
    list:AddChild(row)
    return r
end

local function paintTick(r)
    if not r.tick then return end
    local on = S.ticks[placeKey(r.p)] == true
    -- found places MOVE to the Found drawer; never dimmed or struck through (the GamesOMG panel standard)
    setText(r.mark, on and "✓" or "", COL.accent)
end

local PANEL_W, SHUT = 460, 520          -- width; the slot's x when slid out of sight (alignment = its right edge)
local UI_HINT = "Enter go  ·  ↑ ↓ pick  ·  ← → fold  ·  Esc close"

local function buildPanel(pc)
    local w = makeHost(pc)
    local tree = w.WidgetTree
    local canvas = new("/Script/UMG.CanvasPanel", tree)
    tree.RootWidget = canvas

    -- the right edge, full height, slid in from the side (the Planet Crafter panel's place and feel)
    local panel = box(tree, COL.panel, M(20, 18, 20, 14), 14)
    local slot = canvas:AddChildToCanvas(panel)
    slot:SetAnchors({ Minimum = { X = 1, Y = 0 }, Maximum = { X = 1, Y = 1 } })
    slot:SetAlignment({ X = 1, Y = 0 })
    slot:SetOffsets({ Left = SHUT, Top = 40, Right = PANEL_W, Bottom = 40 })
    local vb = new("/Script/UMG.VerticalBox", tree)
    panel:SetContent(vb)

    local top = new("/Script/UMG.HorizontalBox", tree)
    vadd(vb, top, M(0, 0, 0, 2))
    local mark = box(tree, COL.orange, M(9, 2), 6)
    mark:SetContent(text(tree, "G", 18, lin(0x111111), true))
    hadd(top, mark, M(0, 0, 10, 0))
    hadd(top, text(tree, "GamesOMG Teleport", 21, COL.text, true), nil, true)
    hadd(top, text(tree, SET.PanelKey .. " close", 12, COL.dim), M(10, 0, 0, 0))
    vadd(vb, text(tree, "Pick a place. Go there. Get back.", 14, COL.muted), M(0, 0, 0, 12))

    local field = box(tree, COL.field, M(14, 10), 8)
    vadd(vb, field, M(0, 0, 0, 8))
    local edit = new("/Script/UMG.EditableText", tree)
    pcall(function()
        local st = edit.WidgetStyle
        local font = findFont()
        if font then st.Font.FontObject = font end
        st.Font.Size = 17
        st.ColorAndOpacity = { SpecifiedColor = COL.text, ColorUseRule = 0 }
    end)
    pcall(function() edit.ClearKeyboardFocusOnCommit = false end)
    pcall(function() edit.RevertTextOnEscape = false end)
    edit:SetHintText(FText("Paste a /tp line, or type a place"))   -- one that fits the box (0.2.0's was cut off)
    field:SetContent(edit)

    local btns = new("/Script/UMG.HorizontalBox", tree)
    vadd(vb, btns, M(0, 0, 0, 6))
    local saveBtn = box(tree, COL.btn, M(14, 7), 6)
    saveBtn:SetContent(text(tree, "Save here", 14, COL.text, true))
    pcall(function() saveBtn:SetVisibility(0) end)
    hadd(btns, saveBtn, M(0, 0, 8, 0))
    local backBtn = box(tree, COL.btn, M(14, 7), 6)
    backBtn:SetContent(text(tree, SET.BackKey .. "  Back", 14, COL.text, true))
    pcall(function() backBtn:SetVisibility(0) end)
    hadd(btns, backBtn)

    local status = wrapped(text(tree, UI_HINT, 12, COL.dim))
    vadd(vb, status, M(2, 2, 0, 6))

    local list = new("/Script/UMG.ScrollBox", tree)
    vadd(vb, list, M(0, 0, 0, 8), true)
    vadd(vb, wrapped(text(tree, "Copy Teleport on any pin at\ngamesomg.com/grounded-2-map", 12, COL.dim)), M(2, 0, 0, 0))
    return { w = w, tree = tree, slot = slot, edit = edit, status = status, list = list, saveBtn = saveBtn, backBtn = backBtn, btnC = {} }
end

-- every row, rebuilt when My places changes (a hundred-odd widgets: milliseconds)
local function buildList()
    local ui = S.ui
    ui.list:ClearChildren()
    S.items, S.vis, S.sel, S.hovered = {}, {}, 0, nil
    local secs = sections()
    for _, s in ipairs(secs) do
        if s.mine then s.items = S.saved end
        S.items[#S.items + 1] = headRow(ui.tree, ui.list, s)
        if s.mine and #s.items == 0 then
            S.items[#S.items + 1] = hintRow(ui.tree, ui.list, s, "Type a name above and press Save here (or just press it).")
        end
        for _, p in ipairs(s.items) do
            if s.mine or not S.ticks[placeKey(p)] then
                local r = placeRow(ui.tree, ui.list, s, p)
                paintTick(r)
                S.items[#S.items + 1] = r
            end
        end
    end
    S.items[#S.items + 1] = headRow(ui.tree, ui.list, S.drawer)
    for _, s in ipairs(secs) do
        if s.found then
            local any = false
            for _, p in ipairs(s.items) do
                if S.ticks[placeKey(p)] then
                    if not any then S.items[#S.items + 1] = headRow(ui.tree, ui.list, s.found); any = true end
                    local r = placeRow(ui.tree, ui.list, s.found, p)
                    paintTick(r)
                    S.items[#S.items + 1] = r
                end
            end
        end
    end
end

local function buildToast(pc)
    local w = makeHost(pc)
    local tree = w.WidgetTree
    local canvas = new("/Script/UMG.CanvasPanel", tree)
    tree.RootWidget = canvas
    local b = box(tree, COL.panel, M(18, 10), 10)
    centre(canvas, b, nil, nil, 0.0, 90)
    local hb = new("/Script/UMG.HorizontalBox", tree)
    b:SetContent(hb)
    local mark = box(tree, COL.orange, M(7, 0), 5)
    mark:SetContent(text(tree, "G", 14, lin(0x111111), true))
    hadd(hb, mark, M(0, 0, 10, 0))
    local t = text(tree, "", 17, COL.text)
    hadd(hb, t)
    pcall(function() w:SetVisibility(3) end)
    return { w = w, t = t }
end

-- ── toast: one line at the top of the screen for a few seconds ──────────────────────────────────────────────────────
local function toast(msg, colour, secs)
    log(msg)
    local pc = UEHelpers.GetPlayerController()
    if not (pc and pc:IsValid()) then return end
    if S.toast == false then return end
    if not (S.toast and alive(S.toast.w) and alive(S.toast.t)) then
        local ok, t = pcall(buildToast, pc)
        if not ok then log("toast UI failed: " .. tostring(t)); S.toast = false; return end
        S.toast = t
    end
    setText(S.toast.t, msg, colour or COL.text)
    if not S.toast.w:IsInViewport() then S.toast.w:AddToViewport(1000) end
    S.toast.w:SetVisibility(3)
    S.toastLeft = math.floor((secs or 3.5) * 10)
end

-- ── the list: what shows, selection, hover ──────────────────────────────────────────────────────────────────────────
local function paint()
    for i, r in ipairs(S.vis) do
        local c = (i == S.sel) and COL.sel or (r == S.hovered and COL.hover or COL.clear)
        if r.painted ~= c then r.w:SetBrushColor(c); r.painted = c end
    end
end

local function refresh(q)
    q = (q or ""):lower():gsub("^%s+", ""):gsub("%s+$", "")
    local isTp = q:match("^/?tp%s") ~= nil
    local filt = q ~= "" and not isTp
    local secs = sections()
    local all = { S.drawer }
    for _, s in ipairs(secs) do all[#all + 1] = s; if s.found then all[#all + 1] = s.found end end
    for _, s in ipairs(all) do s.match, s.total = 0, 0 end
    for _, it in ipairs(S.items) do
        if not it.head and not it.hint then
            local s = it.sec
            it.hit = (not filt) or it.key:find(q, 1, true) ~= nil
            s.total = s.total + 1
            if it.hit then s.match = s.match + 1 end
            if s.parent then
                s.parent.total = s.parent.total + 1
                if it.hit then s.parent.match = s.parent.match + 1 end
            end
        end
    end
    local function isOpen(s) return (filt and s.match > 0) or ((not filt) and s.open) end
    local function parentOpen(s) return s.parent == nil or isOpen(s.parent) end
    S.vis = {}
    for _, it in ipairs(S.items) do
        local s, show = it.sec, false
        if it.head then
            if s.drawer then
                show = s.total > 0 and ((not filt) or s.match > 0)
                setText(it.count, filt and (s.match .. " matching") or tostring(s.total))
            else
                show = parentOpen(s) and ((filt and s.match > 0) or ((not filt) and (s.mine or s.total > 0)))
                if s.mine or s.parent then
                    setText(it.count, filt and (s.match .. " matching") or tostring(s.total))
                else
                    local f = s.found and s.found.total or 0
                    setText(it.count, filt and (s.match .. " matching") or (f .. " / " .. (s.total + f) .. " found"))
                end
            end
            setText(it.arrow, isOpen(s) and "–" or "+")
        elseif it.hint then
            show = (not filt) and s.open
        else
            show = parentOpen(s) and it.hit and (filt or s.open)
        end
        it.w:SetVisibility(show and (it.hint and 3 or 0) or 1)
        it.painted = nil
        if show then S.vis[#S.vis + 1] = it end
    end
    if filt then
        -- Enter goes to the place whose NAME you typed, even from the Found drawer, before a place that only shares its
        -- area ("pergola" with Pergola found picked Crow's Nest, in Pergola Area, in 0.2.0)
        S.sel = 0
        local first = 0
        for i, it in ipairs(S.vis) do
            if not it.head and not it.hint then
                if first == 0 then first = i end
                if it.p.n:lower():find(q, 1, true) then S.sel = i; break end
            end
        end
        if S.sel == 0 then S.sel = first end
    elseif S.sel > #S.vis then
        S.sel = #S.vis
    end
    paint()
    if filt and S.sel > 0 and S.ui then
        pcall(function() S.ui.list:ScrollWidgetIntoView(S.vis[S.sel].w, false, 0, 24.0) end)
    end
end

local function status(msg, colour)
    if not S.ui then return end
    setText(S.ui.status, msg or UI_HINT, colour or COL.dim)
end

-- ── /tp lines ───────────────────────────────────────────────────────────────────────────────────────────────────────
local function parseTp(s)
    s = (s or ""):gsub("^%s+", ""):gsub("%s+$", "")
    local body = s:match("^/?[Tt][Pp]%s+(.+)$")
    if not body then return nil end
    local n = {}
    for t in body:gmatch("[^%s,]+") do
        local v = tonumber(t)
        if not v then return nil, "That line has something that isn't a number in it." end
        n[#n + 1] = v
    end
    if #n < 3 or #n > 4 then return nil, "A /tp line is three numbers (and an optional facing): /tp X Y Z" end
    if math.abs(n[1]) > 400000 or math.abs(n[2]) > 400000 or math.abs(n[3]) > 120000 then
        return nil, "Those numbers are outside the yard. Copy the line again from the map."
    end
    return { x = n[1], y = n[2], z = n[3], yaw = n[4] }
end

local function nearestName(x, y, z, radius)
    local best, d2 = nil, (radius or 1500) ^ 2
    for _, p in ipairs(PLACES.places or {}) do
        local dx, dy, dz = p.x - x, p.y - y, (p.z - z) * 2
        local d = dx * dx + dy * dy + dz * dz
        if d < d2 then best, d2 = p, d end
    end
    return best and best.n or nil
end

-- ── open / close: slides in from the right; hidden, never removed (off screen the engine frees it: 11-20) ───────────
S.anim, S.slideX = nil, SHUT
local function slideTo(target, after)
    S.anim = { from = S.slideX or SHUT, to = target, t = 0, n = 9, after = after }
end

local function gameInput(pc)
    pcall(function() wbl():SetInputMode_GameOnly(pc, false) end)
    pcall(function() pc.bShowMouseCursor = S.prevCursor end)
end

local function closePanel()
    if not S.open then return end
    S.open = false
    local pc = UEHelpers.GetPlayerController()
    if pc and pc:IsValid() then gameInput(pc) end
    slideTo(SHUT, function() pcall(function() S.ui.w:SetVisibility(1) end) end)
end

local function openPanel()
    local pc, p = pawnOf()
    if not p then toast("Load into a world first.", COL.bad); return end
    local cursorOn = false
    pcall(function() cursorOn = pc.bShowMouseCursor end)
    if cursorOn and not S.anim then toast("Close the game's menu first, then press " .. SET.PanelKey .. ".", COL.muted); return end
    local changed = useWorld()
    if S.ui and not (alive(S.ui.w) and alive(S.ui.edit) and alive(S.ui.list) and alive(S.ui.slot)) then S.ui = nil end
    if S.ui and changed then
        local okb, errb = pcall(buildList)
        if not okb then log("list rebuild failed: " .. tostring(errb)) end
    end
    if not S.ui then
        local ok, ui = pcall(buildPanel, pc)
        if ok then
            S.ui = ui
            ok, ui = pcall(buildList)
        end
        if not ok then
            log("panel UI failed: " .. tostring(ui))
            S.ui = nil
            toast("The panel couldn't open on this game build: see the mod's log.", COL.bad, 6)
            return
        end
        S.slideX = SHUT
    end
    S.prevCursor = false
    if not S.ui.w:IsInViewport() then S.ui.w:AddToViewport(900) end
    S.ui.w:SetVisibility(4)                            -- SelfHitTestInvisible: its rows take the mouse
    S.open = true
    S.ui.edit:SetText(FText(""))
    S.lastText = ""
    refresh("")
    status()
    slideTo(-18)
    pcall(function() wbl():SetInputMode_UIOnlyEx(pc, S.ui.edit, 0, false) end)
    pcall(function() pc.bShowMouseCursor = true end)
    pcall(function() S.ui.edit:SetKeyboardFocus() end)
end
-- ── the teleport ────────────────────────────────────────────────────────────────────────────────────────────────────
local function hereOf(pc, p)
    local l = p:K2_GetActorLocation()
    local yaw = 0
    pcall(function() yaw = pc:GetControlRotation().Yaw end)
    -- still standing where the last trip landed (within 5 m): Back can say where that was
    local n = nil
    local L = S.lastDest
    if L and (L.x - l.X) ^ 2 + (L.y - l.Y) ^ 2 + (L.z - l.Z) ^ 2 < 500 * 500 then n = L.n end
    return { x = l.X, y = l.Y, z = l.Z, yaw = yaw, n = n }
end

local function move(pc, p, x, y, z, yaw)
    local loc = v3(x, y, z)
    local rot = { Pitch = 0.0, Yaw = (yaw or 0) + 0.0, Roll = 0.0 }
    local auth = true
    pcall(function() auth = p:HasAuthority() end)
    local ok, ret
    if auth then
        ok, ret = pcall(function() return p:TeleportToNocheck(loc, rot) end)
        if not ok or ret == false then
            log("TeleportToNocheck " .. (ok and "returned false" or ("failed: " .. tostring(ret))) .. "; trying K2_TeleportTo")
            ok, ret = pcall(function() return p:K2_TeleportTo(loc, rot) end)
        end
    else
        -- a co-op guest asks the host, the way the game's own teleporters do (untested with a second player)
        ok, ret = pcall(function() p:ServerRequestTeleport(loc, rot) end)
        if ok then ret = true end
    end
    if ok and ret ~= false and yaw then
        pcall(function() pc:SetControlRotation(rot) end)
    end
    return ok and ret ~= false, ret
end

local function teleport(dest, isBack)
    local pc, p = pawnOf()
    if not p then toast("Load into a world first.", COL.bad); return false end
    if not isPlayerChar(p) then toast("Get off your mount or out of the buggy first.", COL.bad); return false end
    if S.pending then toast("Still landing. One second.", COL.muted); return false end
    local from = hereOf(pc, p)
    local ok, why = move(pc, p, dest.x, dest.y, dest.z, dest.yaw)
    if not ok then
        log("teleport refused: " .. tostring(why))
        toast("The game refused that teleport.", COL.bad)
        return false
    end
    S.pending = { dest = dest, from = from, prevBack = S.back, t = 0, phase = "hold", fall = 0, pawn = p:GetFullName(), isBack = isBack }
    S.back = from
    log(string.format("teleport to %s (%.0f %.0f %.0f) from (%.0f %.0f %.0f)", dest.n or "?", dest.x, dest.y, dest.z, from.x, from.y, from.z))
    toast(isBack and "Going back…" or ("Going to " .. (dest.n or "the spot you copied") .. "…"), COL.text, 30)
    return true
end

local function goBack()
    if S.open then closePanel() end
    if not S.back then toast("Nowhere to go back to yet. Teleport somewhere first.", COL.muted); return end
    local b = S.back
    teleport({ x = b.x, y = b.y, z = b.z, yaw = b.yaw, n = b.n }, true)
end

local function refocus(pc)
    pcall(function() wbl():SetInputMode_GameOnly(pc, false) end)
end

-- the landing guard, every tick (100 ms) while a teleport is pending
local function watch()
    local P = S.pending
    if not P then return end
    local pc, p = pawnOf()
    P.t = P.t + 1
    if not p or p:GetFullName() ~= P.pawn then
        -- died or reloaded mid-trip: nothing to judge
        S.pending = nil
        return
    end
    local mode = movementMode(p)
    local z = p:K2_GetActorLocation().Z
    local function bad(why)
        log("landing failed (" .. why .. "): back to the start")
        S.pending = nil
        local f = P.from
        move(pc, p, f.x, f.y, f.z, f.yaw)
        S.back = P.prevBack
        refocus(pc)
        toast("That spot didn't hold, so you're back where you were.", COL.bad, 5)
    end
    if P.phase == "hold" then
        -- the game holds you (movement mode None) while the area streams in, then lets go
        if mode ~= 0 then
            P.phase, P.t = "settle", 0
            refocus(pc)
        elseif P.t > 300 then
            bad("the area never finished loading")
        end
    else
        if mode == 3 then P.fall = P.fall + 1 else P.fall = 0 end
        if z < P.dest.z - 1500 or P.fall > 30 then
            bad(string.format("falling: z %.0f, %d ticks", z, P.fall))
        elseif P.t >= 15 then
            S.pending = nil
            local here = p:K2_GetActorLocation()
            S.lastDest = { x = here.X, y = here.Y, z = here.Z, n = P.dest.n }
            if P.isBack then
                toast((P.dest.n and ("Back at " .. P.dest.n) or "Back where you were") .. ".  " .. SET.BackKey .. " swaps again.", COL.good, 4)
            else
                if P.dest.water then
                    toast("You're at " .. (P.dest.n or "the spot you copied") .. ", under water: suit on?  " .. SET.BackKey .. " takes you back.", COL.accent, 6)
                else
                    toast("You're at " .. (P.dest.n or "the spot you copied") .. ".  " .. SET.BackKey .. " takes you back.", COL.good, 4)
                end
            end
        end
    end
end

-- ── actions from the panel ──────────────────────────────────────────────────────────────────────────────────────────
local function boxText() return (S.ui and S.ui.edit:GetText():ToString()) or "" end

local function saveHere()
    local pc, p = pawnOf()
    if not (p and S.ui) then return end
    local here = hereOf(pc, p)
    local s = boxText():gsub("^%s+", ""):gsub("%s+$", "")
    local name
    if s ~= "" and not s:match("^/?[Tt][Pp]%s") then
        local n = utf8.len(s)                          -- 48 letters, never half of one (a name typed in any language)
        if n == nil then name = s:sub(1, 48)
        elseif n <= 48 then name = s
        else name = s:sub(1, utf8.offset(s, 49) - 1) end
    else
        local near = nearestName(here.x, here.y, here.z, 3000)
        name = near and ("Near " .. near) or ("Spot at " .. neOf(here.x, here.y))
    end
    table.insert(S.saved, 1, { n = name, x = here.x, y = here.y, z = here.z + 5, yaw = here.yaw })
    local ok = saveMine()
    sections()[1].open = true
    S.ui.edit:SetText(FText(""))
    S.lastText = ""
    buildList()
    refresh("")
    status(ok and ("Saved: " .. name) or "Saved for this session; the file couldn't be written (see the log).", ok and COL.good or COL.bad)
    pcall(function() S.ui.edit:SetKeyboardFocus() end)
end

local function deleteSaved(p)
    for i, q in ipairs(S.saved) do
        if q == p then table.remove(S.saved, i); break end
    end
    saveMine()
    local s = boxText()
    buildList()
    refresh(s)
    status("Removed: " .. p.n, COL.muted)
end

local function toggleTick(r)
    local k = placeKey(r.p)
    S.ticks[k] = (not S.ticks[k]) or nil
    saveTicks()
    -- it moves: into Found under its heading, or back up to its group
    local keep = S.sel
    buildList()
    refresh(S.lastText)
    S.sel = math.min(keep, #S.vis)
    paint()
    status((S.ticks[k] and "Found: " or "Back in the list: ") .. r.p.n, COL.muted)
end

local function fold(open)
    local it = S.vis[S.sel]
    if S.open and it and it.head then
        it.sec.open = open
        refresh(S.lastText)
    end
end

local function go()
    if not (S.open and S.ui) then return end
    local s = boxText()
    -- Enter can beat the 100 ms tick (type fast, Enter at once: 11-20 it went to the list's first row)
    if s ~= S.lastText then S.lastText = s; refresh(s) end
    if s:match("^%s*/?[Tt][Pp][%s]") then
        local t, err = parseTp(s)
        if not t then status(err, COL.bad); return end
        t.n = nearestName(t.x, t.y, t.z) or "the spot you copied"
        for _, q in ipairs(PLACES.places or {}) do
            if q.water and math.abs(q.x - t.x) < 2 and math.abs(q.y - t.y) < 2 then t.water = true; break end
        end
        closePanel()
        teleport(t)
        return
    end
    local it = S.vis[S.sel]
    if not it or it.hint then
        status(s ~= "" and "Nothing called that. Try fewer letters, or paste a /tp line." or "Pick a place, or paste a /tp line.", COL.bad)
        return
    end
    if it.head then
        it.sec.open = not it.sec.open
        refresh(s)
        return
    end
    closePanel()
    teleport({ x = it.p.x, y = it.p.y, z = it.p.z, yaw = it.p.yaw, n = it.p.n, water = it.p.water })
end

local function step(d)
    if not S.open or #S.vis == 0 then return end
    local i = S.sel
    repeat i = i + d until i < 1 or i > #S.vis or not S.vis[i].hint
    if i < 1 or i > #S.vis then return end
    S.sel = i
    paint()
    pcall(function() S.ui.list:ScrollWidgetIntoView(S.vis[S.sel].w, false, 0, 24.0) end)
end

local function hov(w)
    local ok, v = pcall(function() return w:IsHovered() end)
    return ok and v == true
end

local function click()
    if not (S.open and S.ui) then return end
    if hov(S.ui.saveBtn) then saveHere(); return end
    if hov(S.ui.backBtn) then goBack(); return end
    for i, it in ipairs(S.vis) do
        if not it.hint and hov(it.w) then
            S.sel = i
            if it.head then
                it.sec.open = not it.sec.open
                refresh(S.lastText)
            elseif it.tick and hov(it.tick) then
                toggleTick(it)
            elseif it.del and hov(it.del) then
                deleteSaved(it.p)
            else
                paint()
                go()
            end
            return
        end
    end
end

-- ── the loops: hover, typing, the landing guard, the toast clock (100 ms); the slide (16 ms, only while it moves) ──
local function tick()
    S.tick = S.tick + 1
    watch()
    if S.toastLeft > 0 then
        S.toastLeft = S.toastLeft - 1
        if S.toastLeft == 0 and S.toast then pcall(function() S.toast.w:SetVisibility(1) end) end
    end
    if S.open and S.ui and not alive(S.ui.edit) then S.open, S.ui = false, nil end
    if S.open and S.ui then
        local s = boxText()
        if s ~= S.lastText then
            S.lastText = s
            refresh(s)
            if s:match("^%s*/?[Tt][Pp][%s]") then
                local t, err = parseTp(s)
                if t then
                    local n = nearestName(t.x, t.y, t.z)
                    status("Enter to go" .. (n and (" to " .. n) or " there") .. ".", COL.good)
                elseif err then status(err, COL.bad) end
            else
                status()
            end
        end
        local h = nil
        for _, it in ipairs(S.vis) do
            if not it.hint and hov(it.w) then h = it; break end
        end
        if h ~= S.hovered then S.hovered = h; paint() end
        for _, b in ipairs({ S.ui.saveBtn, S.ui.backBtn }) do
            local c = hov(b) and COL.btnHover or COL.btn
            if S.ui.btnC[b] ~= c then S.ui.btnC[b] = c; pcall(function() b:SetBrushColor(c) end) end
        end
    end
end

local function slideTick()
    local A = S.anim
    if not A then return end
    if not (S.ui and alive(S.ui.slot)) then S.anim = nil; return end
    A.t = A.t + 1
    local k = math.min(1, A.t / A.n)
    local e = 1 - (1 - k) * (1 - k)
    S.slideX = A.from + (A.to - A.from) * e
    S.ui.slot:SetOffsets({ Left = S.slideX, Top = 40, Right = PANEL_W, Bottom = 40 })
    if k >= 1 then
        S.anim = nil
        if A.after then A.after() end
    end
end

-- ── keys ────────────────────────────────────────────────────────────────────────────────────────────────────────────
local function keyOf(name, fallback)
    local k = Key[name]
    if not k then log("unknown key '" .. tostring(name) .. "' in settings.txt; using " .. fallback); k = Key[fallback] end
    return k
end
local function onGame(fn) return function() ExecuteInGameThread(function()
    local ok, err = pcall(fn)
    if not ok then log("ERROR " .. tostring(err)) end
end) end end

RegisterKeyBind(keyOf(SET.PanelKey, "F7"), onGame(function() if S.open then closePanel() else openPanel() end end))
RegisterKeyBind(keyOf(SET.BackKey, "F8"), onGame(goBack))
RegisterKeyBind(Key.RETURN, onGame(function() if S.open then go() end end))
RegisterKeyBind(Key.ESCAPE, onGame(function() if S.open then closePanel() end end))
RegisterKeyBind(Key.UP_ARROW, onGame(function() step(-1) end))
RegisterKeyBind(Key.DOWN_ARROW, onGame(function() step(1) end))
RegisterKeyBind(Key.LEFT_ARROW, onGame(function() fold(false) end))
RegisterKeyBind(Key.RIGHT_ARROW, onGame(function() fold(true) end))
RegisterKeyBind(Key.LEFT_MOUSE_BUTTON, onGame(click))

LoopInGameThreadWithDelay(100, function()
    local ok, err = pcall(tick)
    if not ok then log("tick ERROR " .. tostring(err)) end
end)
LoopInGameThreadWithDelay(16, function()
    local ok, err = pcall(slideTick)
    if not ok then log("slide ERROR " .. tostring(err)); S.anim = nil end
end)

-- build the panel without a world (the harness's `uitest`: proves the UI path at the main menu)
local function uiTest()
    local pc = UEHelpers.GetPlayerController()
    if not (pc and pc:IsValid()) then log("uitest: no controller"); return end
    local ok, ui = pcall(buildPanel, pc)
    log("uitest: panel " .. tostring(ok) .. (ok and "" or (" " .. tostring(ui))))
    if not ok then return end
    S.ui = ui
    local okl, err = pcall(buildList)
    log("uitest: list " .. tostring(okl) .. " " .. tostring(err) .. ", items " .. #S.items)
    ui.w:AddToViewport(900)
    refresh("")
    log("uitest: closed view " .. #S.vis .. " rows (heads only + My places)")
    refresh("pergola")
    log("uitest: 'pergola' -> " .. #S.vis .. " rows, selected " .. tostring(S.vis[S.sel] and S.vis[S.sel].p and S.vis[S.sel].p.n))
    refresh("")
    ui.w:SetVisibility(1)
    toast("GamesOMG Teleport " .. VERSION .. " ui test", COL.text, 2)
end

-- the test harness, only in a test install (never in the release zip)
GOMG_TP = { uiTest = uiTest, S = S, teleport = teleport, back = goBack, open = openPanel, close = closePanel, go = go, step = step,
            toast = toast, parseTp = parseTp, pawnOf = pawnOf, move = move, movementMode = movementMode, saveHere = saveHere,
            buildList = buildList, toggleTick = toggleTick,
            filter = refresh, log = log, version = VERSION, places = PLACES, findFont = findFont }
pcall(require, "dev")

log(string.format("%s loaded: %d places, %d of mine; %s opens the panel, %s goes back", VERSION,
    #(PLACES.places or {}), #S.saved, SET.PanelKey, SET.BackKey))
