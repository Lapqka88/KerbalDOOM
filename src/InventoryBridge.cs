using System;
using System.Collections.Generic;

namespace KerbalDoom
{
    public static class DoomInventory
    {
        public static readonly string[] Weapons = {"kdPistol", "kdShotgun", "kdSuperShotgun", "kdChaingun", "kdRocketLauncher", "kdPlasma", "kdChainsaw", "kdBfg"};
        public static readonly string[] Ammo = {"kdBullets", "kdShells", "kdRockets", "kdCells"};
        public static readonly int[] PackAmounts = {100, 20, 5, 100};
        public static readonly int[] AmmoCaps = {400, 100, 50, 600};
        public static IEnumerable<ModuleInventoryPart> Containers(Vessel vessel)
        {
            if (vessel == null) yield break;
            foreach (Part part in vessel.parts)
                if (part != null) foreach (ModuleInventoryPart inventory in part.FindModulesImplementing<ModuleInventoryPart>()) yield return inventory;
        }
        public static int Count(Vessel vessel, string item)
        {
            int count = 0;
            foreach (var inventory in Containers(vessel)) count += inventory.TotalAmountOfPartStored(item);
            return count;
        }
        public static bool Consume(Vessel vessel, string item)
        {
            foreach (var inventory in Containers(vessel))
                // Cargo case has no native deploy sound list; playSound=true can throw AFTER deleting the item.
                // Caller plays the WAD sound after successful consumption.
                if (inventory.ContainsPart(item)) return inventory.RemoveNPartsFromInventory(item, 1, false);
            return false;
        }
        public static bool Give(Vessel vessel, string item)
        {
            foreach (var inventory in Containers(vessel))
            {
                AvailablePart available = PartLoader.getPartInfoByName(item);
                if (available == null) return false;
                for (int i = 0; i < inventory.InventorySlots; i++)
                {
                    if (!inventory.IsSlotEmpty(i) && inventory.CanStackInSlot(available, "", i) && inventory.HasStackingSpace(i) && inventory.CheckPartStorage(item, i))
                        return inventory.UpdateStackAmountAtSlot(i, inventory.GetStackAmountAtSlot(i) + 1, "");
                }
                int slot = inventory.FirstEmptySlot();
                if (slot >= 0 && inventory.CheckPartStorage(item, slot)) return inventory.StoreCargoPartAtSlot(item, slot);
            }
            return false;
        }
        public static bool Owns(Vessel vessel, int weapon) { return weapon >= 0 && weapon < Weapons.Length && Count(vessel, Weapons[weapon]) > 0; }
        public static string KerbalKey(Vessel vessel)
        {
            if (vessel == null) return "";
            if (!vessel.isEVA) return "IVA:" + vessel.id;
            foreach (Part part in vessel.parts)
                if (part != null && part.protoModuleCrew.Count > 0) return part.protoModuleCrew[0].name;
            return vessel.vesselName;
        }
    }
}
