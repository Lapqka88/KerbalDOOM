using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KerbalDoom
{
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public sealed class DoomMainMenu : MonoBehaviour
    {
        private DoomGlobalSettings settings;
        private DoomAssets assets;
        private bool open;
        private Rect window = new Rect(50, 50, 620, 590);
        private Vector2 scroll;
        private string status = "", assetPath = "";
        private readonly Dictionary<GraphicRaycaster, bool> nativeRaycasters = new Dictionary<GraphicRaycaster, bool>();
        private const string MenuLock = "KerbalDoom.MainMenu";
        private void Start()
        {
            settings = DoomGlobalSettings.Load().Copy();
            status = settings.LoadError.Length > 0 ? "Ошибка настроек: " + settings.LoadError : "Сохраняются общие настройки мода. Изменения применяются при следующем входе в полёт.";
            TryLoadArt();
        }
        private void TryLoadArt()
        {
            if (assets != null) { assets.Dispose(); assets = null; }
            assetPath = settings.WadPath;
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
            { status = "DOOM2.WAD не найден. Укажи путь ниже: настройки доступны и без WAD."; return; }
            try { assets = new DoomAssets(assetPath); assets.TryTexture("M_DOOM"); }
            catch (Exception e) { status = "Не удалось прочитать WAD: " + e.Message; assets = null; }
        }
        private void OnGUI()
        {
            if (settings == null) return;
            float scale = Mathf.Clamp(Screen.height / 800f, .65f, 1.7f);
            float width = Screen.width / scale, height = Screen.height / scale;
            Matrix4x4 old = GUI.matrix;
            int oldDepth = GUI.depth;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1)); GUI.depth = -1000;
            DoomUi.Begin(assets);
            try
            {
                Rect launch = new Rect(Mathf.Max(15, width - 210), 20, 190, 42);
                if (GUI.Button(launch, "KERBAL DOOM")) open = !open;
                if (open)
                {
                    window.width = Mathf.Min(620, width - 30); window.height = Mathf.Min(620, height - 70);
                    window.x = Mathf.Clamp(window.x, 8, Mathf.Max(8, width - window.width - 8));
                    window.y = Mathf.Clamp(window.y, 65, Mathf.Max(65, height - window.height - 8));
                    window = GUI.Window(618217, window, DrawWindow, "KERBAL DOOM — НАСТРОЙКИ");
                }
            }
            catch (Exception e) { open = false; status = "Ошибка меню: " + e.Message; Debug.LogException(e); }
            finally { CaptureNativeMenu(open); DoomUi.End(); GUI.matrix = old; GUI.depth = oldDepth; }
        }
        private void CaptureNativeMenu(bool value)
        {
            if (!value)
            {
                foreach (var pair in nativeRaycasters) if (pair.Key != null) pair.Key.enabled = pair.Value;
                nativeRaycasters.Clear(); InputLockManager.RemoveControlLock(MenuLock); return;
            }
            InputLockManager.SetControlLock(ControlTypes.All, MenuLock);
            foreach (GraphicRaycaster raycaster in Resources.FindObjectsOfTypeAll<GraphicRaycaster>())
            {
                if (raycaster == null || !raycaster.gameObject.scene.IsValid() || !raycaster.gameObject.activeInHierarchy) continue;
                if (!nativeRaycasters.ContainsKey(raycaster)) nativeRaycasters[raycaster] = raycaster.enabled;
                raycaster.enabled = false;
            }
        }
        private void DrawWindow(int id)
        {
            DoomUi.DrawTitle(assets, new Rect(window.width * .5f - 110, 25, 220, 70));
            GUILayout.Space(75);
            GUILayout.Label("Путь к своей копии DOOM2.WAD");
            settings.WadPath = GUILayout.TextField(settings.WadPath ?? "");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Прочитать WAD / обновить оформление")) TryLoadArt();
            GUILayout.EndHorizontal();
            float areaHeight = Mathf.Max(100, window.height - 425);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(areaHeight));
            settings.Sensitivity = Slider("Чувствительность мыши", settings.Sensitivity, .2f, 6);
            settings.Movement = Slider("Скорость EVA (множитель)", settings.Movement, .5f, 3);
            settings.WeaponSize = Slider("Размер оружия", settings.WeaponSize, .5f, 1.5f);
            settings.ShipDamage = Slider("Урон кораблям (0 — без разрушений)", settings.ShipDamage, 0, 3);
            settings.AmbientSpawns = GUILayout.Toggle(settings.AmbientSpawns, "Случайные встречи вне Кербина / домашнего мира");
            settings.AmbientLimit = Mathf.RoundToInt(Slider("Предел живых врагов", settings.AmbientLimit, 1, 40));
            settings.SpawnInterval = Slider("Интервал встреч, секунд", settings.SpawnInterval, 10, 120);
            settings.LandingGrace = Slider("Подготовка после посадки, секунд", settings.LandingGrace, 5, 120);
            settings.EasterEggs = GUILayout.Toggle(settings.EasterEggs, "Пасхалки и интернет-культура (только шутки, без скрытых читов)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Режим:", GUILayout.Width(60)); settings.ToggleKey = GUILayout.TextField(settings.ToggleKey, GUILayout.Width(70));
            GUILayout.Label("Меню:", GUILayout.Width(60)); settings.MenuKey = GUILayout.TextField(settings.MenuKey, GUILayout.Width(70));
            GUILayout.EndHorizontal();
            GUILayout.Label("Названия клавиш Unity: F8, F7, Insert и т. п. Неверные или совпадающие клавиши заменяются на F8/F7.");
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Сохранить"))
            {
                try
                {
                    settings.Save();
                    status = "Сохранено. Новые параметры применятся при следующем входе в полёт, в том числе в существующем сохранении.";
                    if (!string.Equals(assetPath, settings.WadPath, StringComparison.OrdinalIgnoreCase)) TryLoadArt();
                    settings = settings.Copy();
                }
                catch (Exception e) { status = "Не удалось сохранить: " + e.Message; Debug.LogWarning("[KerbalDoom] Settings save: " + e); }
            }
            if (GUILayout.Button("Стандартные параметры")) { settings.ResetOptions(); status = "Параметры сброшены. Путь WAD и клавиши сохранены; нажми «Сохранить»."; }
            if (GUILayout.Button("Закрыть")) open = false;
            GUILayout.EndHorizontal();
            GUILayout.Label(status, GUILayout.Height(50));
            if (settings.EasterEggs && window.height > 530) GUILayout.Label(DoomEasterEggs.Quip("mainmenu"), GUILayout.Height(35));
            GUI.DragWindow(new Rect(0, 0, window.width, 24));
        }
        private static float Slider(string name, float value, float min, float max)
        {
            GUILayout.Label(name + ": " + value.ToString("0.0"));
            return GUILayout.HorizontalSlider(value, min, max);
        }
        private void OnDestroy() { CaptureNativeMenu(false); if (assets != null) assets.Dispose(); assets = null; }
    }
}
