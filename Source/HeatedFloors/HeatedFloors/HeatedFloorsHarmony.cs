﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.BaseGen;
using RimWorld.Planet;
using UnityEngine;
using UnityEngine.AI;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace HeatedFloors
{
    [StaticConstructorOnStartup]
    internal static class HeatedFloorsHarmony
    {
        static HeatedFloorsHarmony()
        {
            var harmony = new Harmony("rimworld.heatedfloors.smashphil");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            //HarmonyInstance.DEBUG = true;

            harmony.Patch(original: AccessTools.Method(type: typeof(SnowGrid), name: "CanHaveSnow"),
                prefix: new HarmonyMethod(typeof(HeatedFloorsHarmony),
                nameof(CanHaveSnowOnHeatedFloors)));
            harmony.Patch(original: AccessTools.Property(type: typeof(CompPowerTrader), name: nameof(CompPowerTrader.PowerOn)).GetSetMethod(), prefix: null,
                postfix: new HarmonyMethod(typeof(HeatedFloorsHarmony),
                nameof(RemoveSnowFromHeatedFloors)));
            harmony.Patch(original: AccessTools.Method(type: typeof(Frame), name: nameof(Frame.CompleteConstruction)), prefix: null, postfix: null,
                transpiler: new HarmonyMethod(typeof(HeatedFloorsHarmony),
                nameof(PlaceConduitUnderHeatedFloorTranspiler)));
            harmony.Patch(original: AccessTools.Method(type: typeof(Building), name: nameof(Building.Destroy)),
                prefix: new HarmonyMethod(typeof(HeatedFloorsHarmony),
                nameof(DestroyConduitWithHF)));
            harmony.Patch(original: AccessTools.Method(type: typeof(CompPower), name: nameof(CompPower.CompGetGizmosExtra)), prefix: null,
                postfix: new HarmonyMethod(typeof(HeatedFloorsHarmony),
                nameof(RemoveReconnectGizmo)));
            harmony.Patch(original: AccessTools.Method(type: typeof(ThingComp), name: nameof(ThingComp.PostDraw)),
                prefix: new HarmonyMethod(typeof(HeatedFloorsHarmony),
                nameof(LowPowerMode)));
        }

        private static bool CanHaveSnowOnHeatedFloors(Map ___map, ref bool __result, int ind)
        {
            Building building = ___map.edificeGrid[ind];
            if(building != null && building.def.defName == "HeatedFloorThing")
            {
                __result = !building.GetComp<CompPowerTrader>().PowerOn ? true : false;
                return false;
            }
            return true;
        }

        private static void RemoveSnowFromHeatedFloors(CompPowerTrader __instance, bool ___powerOnInt)
        {
            if(__instance != null && __instance.parent.def.defName == "HeatedFloorThing" && ___powerOnInt)
            {
                __instance.parent.Map.snowGrid.SetDepth(__instance.parent.Position, 0f);
            }
        }

        public static IEnumerable<CodeInstruction> PlaceConduitUnderHeatedFloorTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> instructionList = instructions.ToList();

            for (int i = 0; i < instructionList.Count; i++)
            {
                CodeInstruction instruction = instructionList[i];
                if (instruction.Calls(AccessTools.Method(type: typeof(GenSpawn), parameters: new Type[]{typeof(Thing), typeof(IntVec3),
                        typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool) }, name: nameof(GenSpawn.Spawn))))
                {
                    yield return instruction;
                    i += 2;
                    instruction = instructionList[i];
                    yield return new CodeInstruction(opcode: OpCodes.Pop);
                    yield return new CodeInstruction(opcode: OpCodes.Ldarg_0);
                    yield return new CodeInstruction(opcode: OpCodes.Ldloc_0);
                    yield return new CodeInstruction(opcode: OpCodes.Call, operand: AccessTools.Method(typeof(HeatedFloorsHarmony), name: nameof(HeatedFloorsHarmony.PlaceConduitUnderHeatedFloor)));
                }
                yield return instruction;
            }
        }

        public static void PlaceConduitUnderHeatedFloor(Frame __instance, Map map)
        {
            if( (__instance.def.entityDefToBuild as ThingDef).defName == "HeatedFloorThing")
            {
                Thing thing = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PowerConduitHF"));
                thing.SetFactionDirect(__instance.Faction);
                GenSpawn.Spawn(thing, __instance.Position, map, __instance.Rotation, WipeMode.Vanish, false);
            }
        }

        public static void DestroyConduitWithHF(Building __instance, DestroyMode mode)
        {
            Thing thing = __instance?.Position.GetThingList(__instance.Map).Find(x => x.def == DefDatabase<ThingDef>.GetNamed("PowerConduitHF"));

            if (thing != null && mode == DestroyMode.Deconstruct)
            {
                thing.DeSpawn(DestroyMode.Vanish);
            }
        }

        public static void RemoveReconnectGizmo(CompPower __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.parent.def.defName == "HeatedFloorThing")
            {
                List<Gizmo> gizmos = __result.Where(x => !(x is Command_Action)).ToList();
                __result = gizmos;
            }
        }

        public static bool LowPowerMode(CompPowerTrader __instance)
        {
            if(Find.TickManager.TicksGame % 100 == 0 && __instance.parent.def.defName == "HeatedFloorThing")
            {
                if (__instance.parent.Map.weatherManager.SnowRate > 0.5f)
                {
                    __instance.PowerOutput = -1f * __instance.Props.PowerConsumption * (__instance.parent.Map.weatherManager.SnowRate * 10f);
                }
                else
                {
                    __instance.PowerOutput = -1f * __instance.Props.PowerConsumption;
                }
            }
            return true;
        }
    }
}
