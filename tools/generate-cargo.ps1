param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$items = @(
    @('kdPistol','DOOM: пистолет',1,0.5,'Оружие 1. Пули. Использование: F8, клавиша 1.'),
    @('kdShotgun','DOOM: дробовик',1,1,'Оружие 2. Дробь.'),
    @('kdSuperShotgun','DOOM: двустволка',1,1.5,'Оружие 3. Два патрона за выстрел.'),
    @('kdChaingun','DOOM: пулемёт',1,2,'Оружие 4. Пули.'),
    @('kdRocketLauncher','DOOM: ракетомёт',1,3,'Оружие 5. Ракеты. Взрывы повреждают корабли.'),
    @('kdPlasma','DOOM: плазмоган',1,2,'Оружие 6. Энергетические ячейки.'),
    @('kdChainsaw','DOOM: бензопила',1,2,'Оружие 7. Не требует патронов.'),
    @('kdBfg','DOOM: BFG 9000',1,4,'Оружие 8. 40 ячеек на выстрел.'),
    @('kdBullets','DOOM: 100 пуль',10,0.5,'Упаковка боеприпасов. На EVA: R для зарядки выбранного оружия.'),
    @('kdShells','DOOM: 20 патронов дроби',10,0.5,'Упаковка боеприпасов. На EVA: R.'),
    @('kdRockets','DOOM: 5 ракет',5,1,'Упаковка ракет. На EVA: R.'),
    @('kdCells','DOOM: 100 ячеек',10,0.5,'Энергия для плазмогана и BFG. На EVA: R.'),
    @('kdMedkit','DOOM: аптечка',5,0.5,'Восстанавливает 50 боевых HP. На EVA: H или меню F7.'),
    @('kdArmor','DOOM: бронекомплект',3,1,'Даёт 100 брони. Использование: F7, Снаряжение.')
)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$definitions = foreach ($item in $items) {
    $itemName = $item[0]; $itemTitle = $item[1]; $itemStack = $item[2]; $itemVolume = ([double]$item[3]).ToString([Globalization.CultureInfo]::InvariantCulture); $itemDescription = $item[4]
    $itemModel = if ($itemName -eq 'kdMedkit' -or $itemName -eq 'kdArmor') {'Squad/Parts/Cargo/RepairKit/RepairKit'} else {'Squad/Parts/Cargo/ScienceKit/ExperimentKit'}
@"
PART
{
    name = $itemName
    module = Part
    author = KerbalDoom
    MODEL
    {
        model = $itemModel
        scale = 0.35, 0.35, 0.35
    }
    rescaleFactor = 1
    TechRequired = start
    entryCost = 0
    cost = 100
    category = Cargo
    subcategory = 0
    title = $itemTitle
    manufacturer = Kerbal DOOM
    description = $itemDescription Загрузите в штатный инвентарь кабины или кербала. Модель — компактный транспортный кейс.
    attachRules = 0,0,0,0,0
    mass = 0.001
    dragModelType = default
    maximum_drag = 0.1
    minimum_drag = 0.1
    angularDrag = 2
    crashTolerance = 15
    maxTemp = 1200
    tags = doom weapon ammo medkit cargo kerbal
    MODULE
    {
        name = ModuleCargoPart
        packedVolume = $itemVolume
        stackableQuantity = $itemStack
    }
}
"@
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'DoomCargo.cfg'), ($definitions -join "`r`n"), [Text.UTF8Encoding]::new($false))
# Native EVA starts with only a few slots. Leave jetpack/parachute space and allow portable combat supplies.
$evaPatch = @'
@PART[kerbalEVA*]:HAS[@MODULE[ModuleInventoryPart]]:AFTER[Squad]
{
    @MODULE[ModuleInventoryPart]
    {
        @InventorySlots = 16
        @packedVolumeLimit = 100
    }
}
'@
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'EvaInventory.cfg'), $evaPatch, [Text.UTF8Encoding]::new($false))
