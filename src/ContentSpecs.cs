namespace KerbalDoom
{
    public sealed class MonsterSpec
    {
        public string Name, Prefix, AttackSound, DeathSound;
        public int Health, Damage;
        public float Speed, Height, Radius, Cooldown, Range;
        public bool Flying, Hitscan, Boss;
        public char AttackFrame, DeathStart, DeathEnd;
        public static readonly MonsterSpec[] All = {
            new MonsterSpec {Name="Зомби-солдат", Prefix="POSS", Health=20, Damage=8, Speed=1.6f, Height=1.7f, Radius=.3f, Cooldown=1.8f, Range=45, Hitscan=true, AttackFrame='E', DeathStart='H', DeathEnd='L', AttackSound="DSPISTOL", DeathSound="DSPODTH1"},
            new MonsterSpec {Name="Сержант с дробовиком", Prefix="SPOS", Health=30, Damage=18, Speed=1.6f, Height=1.7f, Radius=.3f, Cooldown=2.1f, Range=35, Hitscan=true, AttackFrame='E', DeathStart='H', DeathEnd='L', AttackSound="DSSHOTGN", DeathSound="DSPODTH2"},
            new MonsterSpec {Name="Имп", Prefix="TROO", Health=60, Damage=12, Speed=2f, Height=1.8f, Radius=.35f, Cooldown=1.8f, Range=40, AttackFrame='E', DeathStart='I', DeathEnd='M', AttackSound="DSFIRSHT", DeathSound="DSBGDTH1"},
            new MonsterSpec {Name="Демон Pinky", Prefix="SARG", Health=150, Damage=18, Speed=4.1f, Height=1.8f, Radius=.5f, Cooldown=.85f, Range=1.9f, AttackFrame='E', DeathStart='I', DeathEnd='N', AttackSound="DSSGTATK", DeathSound="DSSGTDTH"},
            new MonsterSpec {Name="Какодемон", Prefix="HEAD", Health=400, Damage=22, Speed=1.8f, Height=2.1f, Radius=.65f, Cooldown=2.5f, Range=55, Flying=true, AttackFrame='B', DeathStart='G', DeathEnd='L', AttackSound="DSFIRSHT", DeathSound="DSCACDTH"},
            new MonsterSpec {Name="Ревенант", Prefix="SKEL", Health=300, Damage=30, Speed=2.6f, Height=2.2f, Radius=.4f, Cooldown=2.7f, Range=60, AttackFrame='J', DeathStart='L', DeathEnd='Q', AttackSound="DSSKEATK", DeathSound="DSSKEDTH"},
            new MonsterSpec {Name="Пулемётчик", Prefix="CPOS", Health=70, Damage=10, Speed=2, Height=1.8f, Radius=.35f, Cooldown=.35f, Range=55, Hitscan=true, AttackFrame='E', DeathStart='H', DeathEnd='N', AttackSound="DSSHOTGN", DeathSound="DSPODTH3"},
            new MonsterSpec {Name="Рыцарь ада", Prefix="BOS2", Health=500, Damage=35, Speed=2.4f, Height=2.5f, Radius=.6f, Cooldown=2, Range=65, AttackFrame='E', DeathStart='I', DeathEnd='O', AttackSound="DSFIRSHT", DeathSound="DSKNTDTH"},
            new MonsterSpec {Name="Кибердемон", Prefix="CYBR", Health=4000, Damage=55, Speed=2.7f, Height=3.5f, Radius=.9f, Cooldown=3, Range=100, Boss=true, AttackFrame='E', DeathStart='H', DeathEnd='P', AttackSound="DSRLAUNC", DeathSound="DSCYBDTH"}
        };
    }
    public sealed class WeaponSpec
    {
        public string Name, Prefix, Sound, Flash;
        public string FireFrames = "ABBA";
        public int Pellets, Cost, AmmoSlot;
        public float Interval, Spread;
        public bool Projectile;
        public static readonly WeaponSpec[] All = {
            new WeaponSpec {Name="ПИСТОЛЕТ", Prefix="PISG", Flash="PISF", FireFrames="ABCBA", Sound="DSPISTOL", Pellets=1, Cost=1, AmmoSlot=0, Interval=.4f, Spread=.25f},
            new WeaponSpec {Name="ДРОБОВИК", Prefix="SHTG", Flash="SHTF", FireFrames="ABCDCBA", Sound="DSSHOTGN", Pellets=7, Cost=1, AmmoSlot=1, Interval=.85f, Spread=4.5f},
            new WeaponSpec {Name="ДВУСТВОЛКА", Prefix="SHT2", FireFrames="ABCDEFGHA", Sound="DSDSHTGN", Pellets=20, Cost=2, AmmoSlot=1, Interval=1.5f, Spread=9f},
            new WeaponSpec {Name="ПУЛЕМЁТ", Prefix="CHGG", Flash="CHGF", Sound="DSPISTOL", Pellets=1, Cost=1, AmmoSlot=0, Interval=.12f, Spread=1.7f},
            new WeaponSpec {Name="РАКЕТОМЁТ", Prefix="MISG", Flash="MISF", Sound="DSRLAUNC", Cost=1, AmmoSlot=2, Interval=.8f, Projectile=true},
            new WeaponSpec {Name="ПЛАЗМОГАН", Prefix="PLSG", Flash="PLSF", Sound="DSPLASMA", Cost=1, AmmoSlot=3, Interval=.13f, Projectile=true},
            new WeaponSpec {Name="БЕНЗОПИЛА", Prefix="SAWG", FireFrames="CDCD", Sound="DSSAWHIT", Pellets=1, Cost=0, AmmoSlot=0, Interval=.15f, Spread=0},
            new WeaponSpec {Name="BFG 9000", Prefix="BFGG", Flash="BFGF", Sound="DSBFG", Cost=40, AmmoSlot=3, Interval=1.5f, Projectile=true}
        };
    }
}
