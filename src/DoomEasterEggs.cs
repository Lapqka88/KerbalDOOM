using System;
using UnityEngine;

namespace KerbalDoom
{
    // Text-only, optional references. Never changes HP, ammo, vessels, physics or input bindings.
    public static class DoomEasterEggs
    {
        private static float nextNotice;
        private static int lastAmmo = -1, lastKills;
        private static string typed = "";
        private static readonly string[] menuQuips = {
            "Да, эта ракета запускает DOOM.",
            "All your orbit are belong to us.",
            "Ответ: 42. Вопрос всё ещё на орбите.",
            "404: аэродинамика не найдена. Демоны найдены.",
            "За пределами атмосферы никто не услышит твой ping."
        };
        public static string Quip(string eventName)
        {
            switch (eventName ?? "")
            {
                case "empty": return "404: боезапас не найден. Проверь инвентарь, а не роутер.";
                case "respect": return "F. Уважение экипажу и всем разгонным ступеням.";
                case "42": return "42. Ответ на главный вопрос жизни, вселенной и этой волны.";
                case "69": return "69 устранений. Центр управления одобрительно кивает.";
                case "1337": return "1337. Орбитальный уровень мастерства подтверждён.";
                case "iddqd": return "IDDQD распознано. Бессмертие включается открыто в меню — магии за спиной нет.";
                case "orbit": return "Можно ли запустить DOOM на ракете? Телеметрия отвечает: да.";
                case "map": return "Космический аппарат в DOOM. Кажется, установка системных требований пошла не по плану.";
                default: return menuQuips[(int)((DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond / 12) % menuQuips.Length)];
            }
        }
        public static void Tick(bool modeActive, int ammo, int kills, Action<string> notify)
        {
            if (!modeActive || notify == null || !DoomGlobalSettings.Current.EasterEggs) return;
            string message = null;
            if (Input.GetKeyDown(KeyCode.F) && GUIUtility.keyboardControl == 0) message = Quip("respect");
            if (ammo == 0 && lastAmmo > 0) message = Quip("empty");
            foreach (int milestone in new [] {42, 69, 1337})
                if (kills >= milestone && lastKills < milestone) message = Quip(milestone.ToString());
            lastAmmo = ammo; lastKills = Math.Max(lastKills, kills);
            if (GUIUtility.keyboardControl == 0)
            {
                foreach (char c in Input.inputString)
                    if (char.IsLetter(c)) { typed += char.ToUpperInvariant(c); if (typed.Length > 5) typed = typed.Substring(typed.Length - 5); }
                if (typed == "IDDQD") { message = Quip("iddqd"); typed = ""; }
            }
            if (message != null && Time.realtimeSinceStartup >= nextNotice)
            { nextNotice = Time.realtimeSinceStartup + 14; notify(message); }
        }
        public static void Reset() { typed = ""; lastAmmo = -1; lastKills = 0; nextNotice = 0; }
    }
}
