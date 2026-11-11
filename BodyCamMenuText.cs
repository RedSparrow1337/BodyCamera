using System.Collections.Generic;

namespace BodyCamera
{
    internal static class BodyCamMenuText
    {
        internal static readonly Dictionary<string, string[]> Tabs = new Dictionary<string, string[]>
        {
            { Plugin.SecGeneral,  new[] { "Основное",          "General" } },
            { Plugin.SecPosition, new[] { "Положение камеры",  "Camera Position" } },
            { Plugin.SecRotation, new[] { "Поворот камеры",    "Camera Rotation" } },
            { Plugin.SecMotion,   new[] { "Движение камеры",   "Camera Motion" } },
            { Plugin.SecWeapon,   new[] { "Оружие",            "Weapon" } },
            { Plugin.SecOverlay,  new[] { "Оверлей",           "Overlay" } },
            { Plugin.SecLens,     new[] { "Линза",             "Lens" } },
            { Plugin.SecSounds,   new[] { "Звуки",             "Sounds" } },
            { Plugin.SecBody,     new[] { "Тело",              "Body" } },
            { Plugin.SecDebug,    new[] { "Отладка",           "Debug" } },
        };

        internal static readonly Dictionary<string, string[][]> Groups = new Dictionary<string, string[][]>
        {
            { Plugin.SecGeneral, new[] {
                new[] { "КАМЕРА", "CAMERA", "Enabled", "Toggle Key", "Menu Key", "Menu Color" },
                new[] { "КАМЕРА НА ОРУЖИИ", "WEAPON CAMERA", "Weapon Camera Key" },
                new[] { "АНИМАЦИЯ ВКЛЮЧЕНИЯ", "TOGGLE ANIMATION", "Toggle Animation", "Toggle Animation Strength", "Toggle Tilt Up", "Toggle Hand", "Random Readjust" } } },
            { Plugin.SecPosition, new[] {
                new[] { "ГРУДЬ", "CHEST", "Offset X", "Offset Y", "Offset Z" },
                new[] { "ГРУДЬ, ПИСТОЛЕТ", "CHEST, PISTOL", "Pistol Offset X", "Pistol Offset Y", "Pistol Offset Z" },
                new[] { "ОТ БЕДРА", "HIP AIM", "Hip Aim Offset Up", "Hip Aim Offset Side", "Hip Aim Offset Roll" } } },
            { Plugin.SecRotation, new[] {
                new[] { "КАМЕРА", "CAMERA", "Keep Camera Rotation", "Rotation Inertia", "Dead Zone Mode", "Camera Dead Zone", "Weapon Dead Zone", "Camera Look Down Limit", "Lean Roll Strength" } } },
            { Plugin.SecMotion, new[] {
                new[] { "ОБЩЕЕ", "OVERALL", "Camera Shake Multiplier", "Motion Smoothing" },
                new[] { "ШАГИ", "STEPS", "Step Nod", "ADS Step Nod", "Step Roll Strength", "Random Sway", "Idle Shake", "Idle Shake In Motion" },
                new[] { "ПОВОРОТЫ И НАКЛОН", "TURNS & TILT", "Turn Roll", "Look Shake", "Body Tilt Strength" },
                new[] { "ВЫСТРЕЛ", "SHOOTING", "Shot Reaction 1", "Shot Reaction 2" },
                new[] { "ИМПАКТ", "IMPACTS", "Hit Impact", "Explosion Impact", "Fall Impact" } } },
            { Plugin.SecWeapon, new[] {
                new[] { "ОТ БЕДРА", "HIP", "Idle Motion", "Walk Motion", "Walk Slow", "Walk Normal", "Walk Fast", "Walk Sprint" },
                new[] { "В ПРИЦЕЛЕ", "AIMING", "ADS Idle Motion", "ADS Walk Motion", "ADS Walk Slow", "ADS Walk Normal", "ADS Walk Fast" },
                new[] { "ПРУЖИНА", "SPRING", "Weapon Motion Spring" },
                new[] { "ИНЕРЦИЯ", "INERTIA", "Weapon Inertia Hip", "Weapon Inertia ADS", "Weapon Inertia Pistol Hip", "Weapon Inertia Pistol ADS" },
                new[] { "НАКЛОН У УГЛОВ", "CORNER TILT", "Corner Tilt", "Corner Tilt Speed" },
                new[] { "СТРЕЛЬБА ОТ БЕДРА", "HIP FIRE", "Fire Raise" },
                new[] { "БЛИЖНИЙ БОЙ", "CLOSE COMBAT", "Muzzle Shove" },
                new[] { "ПЕРЕЗАРЯДКА И ПРОВЕРКА", "RELOAD & CHECKS", "Handling Camera", "Mag Check Look" } } },
            { Plugin.SecOverlay, new[] {
                new[] { "НАДПИСИ", "TEXT", "Overlay Style", "Overlay Enabled", "Show Cam Connected", "Overlay Name", "Device Text", "Use Raid Time", "Show Mag Check Count" },
                new[] { "ВИД", "LOOK", "Overlay Scale", "Overlay Curve" },
                new[] { "БАТАРЕЯ", "BATTERY", "Battery Drain" },
                new[] { "ЗВУКИ", "SOUNDS", "Overlay Sound", "Toggle Sound", "Toggle Sound Volume" } } },
            { Plugin.SecLens, new[] {
                new[] { "ЛИНЗА", "LENS", "Lens Effects", "Fisheye Horizontal", "Fisheye Vertical", "Radial Blur" },
                new[] { "ВИНЬЕТКА", "VIGNETTE", "Vignette Strength", "Vignette Proportion", "Vignette Tint", "Vignette Edge Width", "Vignette Movement", "Vignette Step Movement" },
                new[] { "ГРЯЗЬ", "DIRT", "Lens Dirt", "Lens Dirt Scale" } } },
            { Plugin.SecSounds, new[] {
                new[] { "МИКРОФОН", "MICROPHONE", "Camera Mic" },
                new[] { "ДЫХАНИЕ", "BREATHING", "Breathing Volume" },
                new[] { "ВЕТЕР", "WIND", "Wind", "Wind Volume" },
                new[] { "ПОВОРОТЫ", "TURNS", "Turn Sounds", "Turn Sounds Volume" },
                new[] { "ШАГИ", "STEPS", "Step Sounds", "Step Sounds Volume" },
                new[] { "СТРЕЛЬБА", "SHOOTING", "Mic Tremor", "Mic Tremor Volume" } } },
            { Plugin.SecBody, new[] {
                new[] { "3-Е ЛИЦО", "THIRD PERSON", "Third Person Key", "Shoulder Swap Key", "Third Person ADS Chest", "Third Person Vision Effects",
                        "Third Person X", "Third Person Y", "Third Person Z",
                        "Third Person Inertia", "Third Person Shake" },
                new[] { "ЧТО ВИДНО: 3-Е ЛИЦО", "WHAT IS SEEN: THIRD PERSON", "Show Torso", "Show Armor", "Show Head", "Show Helmet", "Show Backpack", "Show Weapons" },
                new[] { "ЧТО ВИДНО: 1-Е ЛИЦО", "WHAT IS SEEN: FIRST PERSON", "First Person Torso", "First Person Armor", "First Person Backpack", "First Person Weapons" } } },
            { Plugin.SecDebug, new[] {
                new[] { "", "", "Debug Logging", "Lens Flip Picture", "Lens Flip Overlay" } } },
        };

        internal static readonly string[] Close = { "ЗАКРЫТЬ", "CLOSE" };
        internal static readonly string[] PressKey = { "Нажми клавишу", "Press a key" };
        internal static readonly string[] Empty = { "пусто", "empty" };
        internal static readonly string[] Default = { "по умолчанию", "default" };
        internal static readonly string[] On = { "ВКЛ", "ON" };
        internal static readonly string[] Off = { "ВЫКЛ", "OFF" };

        internal static readonly string[] Profiles = { "ПРОФИЛИ", "PROFILES" };
        internal static readonly string[] Slot = { "Профиль", "Profile" };
        internal static readonly string[] Save = { "Сохранить", "Save" };
        internal static readonly string[] Load = { "Загрузить", "Load" };
        internal static readonly string[] Saved = { "сохранено", "saved" };
        internal static readonly string[] Loaded = { "загружено", "loaded" };
        internal static readonly string[] ProfileDesc = { "", "" };

        internal static readonly Dictionary<string, string[]> Desc = new Dictionary<string, string[]>
        {
            { "Enabled", new[] { "Вкл / выкл нательную камеру.", "Body camera on / off." } },
            { "Menu Key", new[] { "Открыть / закрыть меню.", "Open / close this menu." } },
            { "Menu Color", new[] { "Цвет оформления меню.", "Menu colour." } },
            { "Toggle Animation", new[] { "Рука поправляет камеру при вкл / выкл.", "A hand adjusts the camera on / off." } },
            { "Toggle Animation Strength", new[] { "Сила рывка в анимации.", "Jolt strength of the animation." } },
            { "Toggle Tilt Up", new[] { "Подъём камеры в анимации (°).", "Camera lift in the animation (°)." } },
            { "Random Readjust", new[] { "Иногда рука поправляет камеру сама.", "Now and then a hand adjusts the camera." } },
            { "Toggle Hand", new[] { "Левая рука тянется к камере (тест).", "Left hand reaches for the camera (test)." } },
            { "Hip Aim Offset Up", new[] { "Камера выше / ниже оружия от бедра (°).", "Camera above / below the gun, hip (°)." } },
            { "Hip Aim Offset Side", new[] { "Камера левее / правее оружия от бедра (°).", "Camera left / right of the gun, hip (°)." } },
            { "Hip Aim Offset Roll", new[] { "Наклон картинки от бедра (°, + по часовой).", "Picture tilt from the hip (°, + clockwise)." } },
            { "Weapon Camera Key", new[] { "Камера на стволе, смотрит на тебя.", "Camera on the barrel, facing you." } },
            { "Third Person Key", new[] { "Камера от 3-го лица.", "Third person camera." } },
            { "Shoulder Swap Key", new[] { "Камера к другому плечу.", "Camera to the other shoulder." } },
            { "Third Person Inertia", new[] { "Отставание камеры от бедра (0 = нет).", "Camera lag from the hip (0 = none)." } },
            { "Third Person Shake", new[] { "Сила тряски камеры.", "Camera shake strength." } },
            { "Third Person ADS Chest", new[] { "В прицеле камера на груди.", "Aiming puts the camera on the chest." } },
            { "Third Person Vision Effects", new[] { "ПНВ / тепловизор в 3-м лице.", "Night vision / thermal in third person." } },
            { "Keep Camera Rotation", new[] { "Вкл — на груди, выкл — как в Таркове.", "On — chest, off — like Tarkov." } },
            { "Rotation Inertia", new[] { "Скорость поворота камеры за взглядом.", "How fast the camera follows the look." } },
            { "Weapon Inertia Hip", new[] { "Отставание оружия от бедра.", "Gun lag from the hip." } },
            { "Weapon Inertia ADS", new[] { "Отставание оружия в прицеле.", "Gun lag while aiming." } },
            { "Weapon Inertia Pistol Hip", new[] { "Отставание пистолета от бедра.", "Pistol lag from the hip." } },
            { "Weapon Inertia Pistol ADS", new[] { "Отставание пистолета в прицеле.", "Pistol lag while aiming." } },
            { "Camera Look Down Limit", new[] { "Предел взгляда вниз (°).", "Look down limit (°)." } },
            { "Lean Roll Strength", new[] { "Крен картинки при наклоне Q / E.", "Picture roll when leaning Q / E." } },
            { "Camera Shake Multiplier", new[] { "Общая сила тряски.", "Overall shake strength." } },
            { "Motion Smoothing", new[] { "Мягкость тряски при ходьбе.", "Softer walking shake." } },
            { "Step Nod", new[] { "Кивок на каждый шаг.", "Nod on every step." } },
            { "ADS Step Nod", new[] { "Кивок на шаг в прицеле.", "Step nod while aiming." } },
            { "Idle Shake", new[] { "Тряска стоя на месте.", "Shake while standing." } },
            { "Idle Shake In Motion", new[] { "Тряска стоя и в движении.", "Idle shake also when moving." } },
            { "Hit Impact", new[] { "Рывок при попадании в тебя.", "Jolt when you are hit." } },
            { "Explosion Impact", new[] { "Тряска от взрыва (до 50 м).", "Explosion shake (up to 50 m)." } },
            { "Fall Impact", new[] { "Тряска при приземлении.", "Shake on landing." } },
            { "Random Sway", new[] { "Случайное блуждание в движении.", "Random sway when moving." } },
            { "Step Roll Strength", new[] { "Качание вбок на каждый шаг.", "Side rock on every step." } },
            { "Dead Zone Mode", new[] { "Выкл, стоит камера или стоит оружие.", "Off, camera stays or gun stays." } },
            { "Weapon Dead Zone", new[] { "Камера ходит, оружие стоит (°).", "Camera moves, gun stays (°)." } },
            { "Turn Roll", new[] { "Крен наружу при повороте.", "Roll outward in turns." } },
            { "Look Shake", new[] { "Дрожание при движении мыши.", "Shake while moving the mouse." } },
            { "Body Tilt Strength", new[] { "Наклон камеры вместе с телом.", "Camera tilts with the body." } },
            { "Shot Reaction 1", new[] { "Реакция камеры на каждый выстрел.", "Camera reaction to every shot." } },
            { "Shot Reaction 2", new[] { "Реакция камеры на каждый выстрел.", "Camera reaction to every shot." } },
            { "Idle Motion", new[] { "Покачивание оружия стоя.", "Gun sway standing." } },
            { "Walk Motion", new[] { "Покачивание оружия при ходьбе.", "Gun sway walking." } },
            { "ADS Idle Motion", new[] { "Покачивание стоя в прицеле.", "Gun sway standing, aiming." } },
            { "ADS Walk Motion", new[] { "Покачивание при ходьбе в прицеле.", "Gun sway walking, aiming." } },
            { "Weapon Motion Spring", new[] { "Покачивание с запаздыванием.", "Gun sway with a little lag." } },
            { "Walk Slow", new[] { "Медленный шаг (× Walk Motion).", "Slow walk (× Walk Motion)." } },
            { "Walk Normal", new[] { "Обычный шаг (× Walk Motion).", "Normal walk (× Walk Motion)." } },
            { "Walk Fast", new[] { "Быстрый шаг (× Walk Motion).", "Fast walk (× Walk Motion)." } },
            { "Walk Sprint", new[] { "Спринт (× Walk Motion).", "Sprint (× Walk Motion)." } },
            { "ADS Walk Slow", new[] { "Медленный шаг в прицеле.", "Slow walk, aiming." } },
            { "ADS Walk Normal", new[] { "Обычный шаг в прицеле.", "Normal walk, aiming." } },
            { "ADS Walk Fast", new[] { "Быстрый шаг в прицеле.", "Fast walk, aiming." } },
            { "Corner Tilt", new[] { "Наклон оружия у углов (°).", "Gun tilt at corners (°)." } },
            { "Fire Raise", new[] { "Пистолет поднимается при стрельбе.", "Pistol rises when firing." } },
            { "Handling Camera", new[] { "Камера за руками при перезарядке и проверке.", "Camera follows the hands on reload and checks." } },
            { "Mag Check Look", new[] { "Взгляд на магазин при проверке.", "Look at the mag when checking." } },
            { "Muzzle Shove", new[] { "Толчок дулом вплотную.", "Muzzle shove up close." } },
            { "Corner Tilt Speed", new[] { "Скорость наклона у углов.", "Corner tilt speed." } },
            { "Overlay Enabled", new[] { "Надписи оверлея.", "Overlay text." } },
            { "Show Cam Connected", new[] { "Надпись CAM CONNECTED.", "CAM CONNECTED text." } },
            { "Show Mag Check Count", new[] { "Число патронов при проверке.", "Round count when checking." } },
            { "Device Text", new[] { "Строка устройства (v2).", "Device line (v2)." } },
            { "Battery Drain", new[] { "Минут на деление батареи.", "Minutes per battery bar." } },
            { "Overlay Curve", new[] { "Изгиб надписей с линзой.", "Text bends with the lens." } },
            { "Lens Effects", new[] { "Эффекты линзы.", "Lens effects." } },
            { "Vignette Proportion", new[] { "0 — круг, 1 — овал.", "0 — circle, 1 — oval." } },
            { "Vignette Edge Width", new[] { "Мягкость края.", "Edge softness." } },
            { "Vignette Step Movement", new[] { "Сдвиг на шаг.", "Shift on steps." } },
            { "Wind", new[] { "Ветер в микрофоне.", "Wind on the mic." } },
            { "Turn Sounds", new[] { "Шорох при повороте.", "Rustle on turns." } },
            { "Step Sounds", new[] { "Стук крепления на шаг.", "Mount knock on steps." } },
            { "Mic Tremor", new[] { "Дребезг микрофона при стрельбе.", "Mic rattle when firing." } },
            { "Camera Mic", new[] { "Звук через микрофон камеры.", "Sound through the camera mic." } },
        };
    }
}
