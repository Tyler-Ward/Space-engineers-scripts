using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;
using VRage.Reflection;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region mdk preserve
        /****************************
 * program settings
 ****************************/

        //automaticaly shit off drills when limit reached
        bool drillAutoShutoff = true;

        //name tag to light or other enablable block to enable when full
        string fullIndicatorNameTag = "[Ind full]";

        //screens to display information on
        bool displayOnCockpits = true;  //if true shows on left cockpit display
        bool displayOnProgramableBlock = true;  //if true shows on programable block display

        //group of thrusters to use for lift force calculation
        //if set to emptystring it will autodetect downwards facing thrusters
        //for space miners this can also be used to set a diferent direction of thrusters and use that for the aceleration limit
        string thrusterGroupName = "";

        //reserved upwards aceleration in m/s (miner will be considered full if upwards aceleration drops below this)
        double reservedUpwardsAceleration = 2.0;

        //static thrust parameters (set these for ships that enter and leave gravity wells to maintain a weight limit when outside a gravity well)
        bool useStaticThrust = false; //if true uses preset thruster performance and gravity strength
        float gravityStrength = 1;  //gravity strength
        //performance of each thruster type at target altitude (set to 0 if selected thruster type wont be used and autodetecting lift thrusters)
        //https://se-speed.ga/ has a calculator for this if needed
        float atmosphericThrusterPerformace = 0.85f; 
        float hydrogenThrusterPerformance = 1f;
        float ionThrusterPerformance = 0.30f;

        /****************************
 * program below this point
 ****************************/
        #endregion

        List<IMyShipDrill> drills = new List<IMyShipDrill>();
        List<IMyTerminalBlock> oreStorage = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> indicator_full = new List<IMyTerminalBlock>();
        List<IMyCockpit> cockpits = new List<IMyCockpit>();

        List<IMyThrust> upThrusters = new List<IMyThrust>();
        List<IMyThrust> allThrusters = new List<IMyThrust>();

        string[] spinnerSymbols = { "/", "-", "\\", "|" };
        int count = 0;

        public Program()
        {
            //run every 10 ticks
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

            //populate the ship components
            UpdateShip();
        }

        public void Main(string argument, UpdateType updateSource)
        {

            count += 1;
            Echo($"Miner overload protection { spinnerSymbols[count % 4]}");

            //only update the ship components periodicaly ~ every 10s at simspeed 1
            if(count % (6*10) == 0)
            {
                UpdateShip();
            }

            //log ship information to console
            Echo("Detected Ship components");
            Echo($"{cockpits.Count} Cockpits");
            Echo($"{upThrusters.Count} Lifting thrusters");
            Echo($"{drills.Count} drills");
            Echo($"{oreStorage.Count-drills.Count} Cargo containers");


            bool full = false;  //set to true if weight or cargo are full

            //calulate weight capacity

            float maxThrust = 0;
            foreach(var thruster in upThrusters)
            {
                if (useStaticThrust)
                {
                    if (thruster.IsFunctional)
                    {
                        float thrusterThrust = thruster.MaxThrust;

                        //apply performance reduction
                        string thrusterType = thruster.BlockDefinition.SubtypeId;
                        if (thrusterType.Contains("AtmosphericThrust"))
                        {
                            thrusterThrust *= atmosphericThrusterPerformace;
                        }
                        else
                        {
                            if (thrusterType.Contains("HydrogenThrust"))
                            {
                                thrusterThrust *= hydrogenThrusterPerformance;
                            }
                            else //ion thrusters dont have a searchable tag so check if not hydrogen instead
                            {
                                thrusterThrust *= ionThrusterPerformance;
                            }
                        }

                        maxThrust += thrusterThrust;
                    }
                }
                else
                {
                    //calculate current thrust
                    if (thruster.IsWorking)
                        maxThrust += thruster.MaxEffectiveThrust;
                }
            }

            double gravity = cockpits[0].GetTotalGravity().Length();
            if (useStaticThrust)
                gravity = gravityStrength * 9.81;

            float shipBaseWeight = cockpits[0].CalculateShipMass().BaseMass;
            float shipweight = cockpits[0].CalculateShipMass().PhysicalMass;
            float maxweight = (float) (maxThrust / (gravity + reservedUpwardsAceleration));

            double requiredThrust = (gravity + reservedUpwardsAceleration) * shipweight;

            Echo("\nWeight Information");
            Echo($"Empty weight {shipBaseWeight:F0}");
            Echo($"Total Weight {shipweight:F0}");
            Echo($"Max Weight {maxweight:F0}");

            if (maxThrust<requiredThrust)
            {
                Echo("overweight");
                full = true;
            }

            // Calculate cargo capacity

            MyFixedPoint cargoSpaceTotal = 0;
            MyFixedPoint cargoSpaceUsed = 0;
            foreach (var container in oreStorage) //this also includes drills
            {
                IMyInventory inv = container.GetInventory();
                cargoSpaceTotal += inv.MaxVolume;
                cargoSpaceUsed += inv.CurrentVolume;
            }

            Echo("\nCargo Information");
            Echo($"Cargo used {(float)cargoSpaceUsed:F2}");
            Echo($"Cargo max {(float)cargoSpaceTotal:F2}");
            //drills dont always fill fully before being unable to accept ore so add a slight cargo space buffer per drill
            MyFixedPoint drillCargoBuffer = (MyFixedPoint)(drills.Count * 0.075);
            if (cargoSpaceUsed >= (cargoSpaceTotal - drillCargoBuffer))
            {
                Echo("Cargo Full");
                full = true;
            }

            //if full
            if (full)
            {
                //turn on full indicators
                foreach (var indicator in indicator_full)
                {
                    IMyFunctionalBlock light = indicator as IMyFunctionalBlock;
                    light.Enabled = true;
                }
                //shitdown drills if set to.
                if (drillAutoShutoff)
                {
                    foreach (var drill in drills)
                    {
                        drill.Enabled = false;
                    }
                }
            }
            else
            {
                //turn off full indicators
                foreach (var indicator in indicator_full)
                {
                    IMyFunctionalBlock light = indicator as IMyFunctionalBlock;
                    light.Enabled = false;
                }
            }

            //display information
            if (displayOnCockpits)
            {
                foreach (var cockpit in cockpits)
                {
                    if (cockpit.SurfaceCount > 0)
                    {
                        IMyTextSurface statusDisplay = cockpits[0].GetSurface(1);
                        statusDisplay.ContentType = ContentType.TEXT_AND_IMAGE;
                        statusDisplay.FontSize = 1.6f;
                        StringBuilder cockpitDisplay = new StringBuilder($"Miner overload protection {spinnerSymbols[count % 4]}\n");

                        cockpitDisplay.Append($"Cargo space {(float)cargoSpaceUsed:F2}/{(float)cargoSpaceTotal:F2}\n");

                        cockpitDisplay.Append(CreateBar(statusDisplay.FontSize, (float)cargoSpaceUsed / (float)cargoSpaceTotal));

                        cockpitDisplay.Append($"Weight {shipweight:F0}/{maxweight:F0}\n");

                        cockpitDisplay.Append(CreateBar(statusDisplay.FontSize, shipweight / maxweight));

                        statusDisplay.WriteText(cockpitDisplay.ToString(), false);
                    }
                }
            }
            if (displayOnProgramableBlock)
            {
                IMyTextSurface statusDisplay = Me.GetSurface(0);
                statusDisplay.ContentType = ContentType.TEXT_AND_IMAGE;
                StringBuilder cockpitDisplay = new StringBuilder($"Miner overload protection {spinnerSymbols[count % 4]}\n");

                cockpitDisplay.Append($"Cargo space {(float)cargoSpaceUsed:F2}/{(float)cargoSpaceTotal:F2}\n");

                cockpitDisplay.Append(CreateBar(statusDisplay.FontSize, (float)cargoSpaceUsed / (float)cargoSpaceTotal));

                cockpitDisplay.Append($"Weight {shipweight:F0}/{maxweight:F0}\n");

                cockpitDisplay.Append(CreateBar(statusDisplay.FontSize, shipweight / maxweight));

                statusDisplay.WriteText(cockpitDisplay.ToString(), false);
            }
        }

        /// <summary>
        /// Updates the list of components used by the script
        /// </summary>
        private void UpdateShip()
        {
            //find objects
            GridTerminalSystem.GetBlocksOfType(drills, drill => drill.IsSameConstructAs(Me));
            GridTerminalSystem.GetBlocksOfType(oreStorage, container => container.IsSameConstructAs(Me) && IsValidOreStorage(container));
            GridTerminalSystem.SearchBlocksOfName(fullIndicatorNameTag, indicator_full, item => item.IsSameConstructAs(Me) && item is IMyFunctionalBlock);
            GridTerminalSystem.GetBlocksOfType(cockpits, item => item.IsSameConstructAs(Me));

            if (thrusterGroupName == "")
            {
                //If there is no pilot in the ship the lifting thrusters can't be automaticaly detected.
                //As a workaround check if thrusters have a valid GridThrustDirection before updating the upThrusters list.
                GridTerminalSystem.GetBlocksOfType(allThrusters, item => item.IsSameConstructAs(Me));
                if (allThrusters.Count() > 0)
                {
                    if (allThrusters[0].GridThrustDirection != Vector3I.Zero)    //if no vector there is no user so keep previous thruster group
                        GridTerminalSystem.GetBlocksOfType(upThrusters, thruster => thruster.IsSameConstructAs(Me) && thruster.GridThrustDirection.Y == -1);
                }
            }
            else
            {
                GridTerminalSystem.GetBlockGroupWithName(thrusterGroupName).GetBlocksOfType(upThrusters, thruster => thruster.IsSameConstructAs(Me));
            }
        }


        /// <summary>
        /// Checks if a container can both store ore and have ore transfered into it by a drill
        /// </summary>
        /// <param name="container">container to check as an ore delivery target</param>
        /// <returns></returns>
        private bool IsValidOreStorage(IMyTerminalBlock container)
        {
            //if item does not have an inventory exit now
            if (!container.HasInventory)
                return false;

            //get the list of items that can be stored
            List<MyItemType> canStore = new List<MyItemType>();
            container.GetInventory().GetAcceptedItems(canStore);

            //check that one of the items is ore (exclude ice to avoid H2/O2 generators)
            bool holdsOre = false;
            foreach (var item in canStore)
            {
                if (item.TypeId == "MyObjectBuilder_Ore" && item.SubtypeId!="Ice")
                {
                    holdsOre = true;
                    break;
                }   
            }

            //reject container if it cant store ore
            if (holdsOre == false)
                return false;

            //check that the container is accessable by the drills
            foreach(var drill in drills)
            {
                if(drill.GetInventory().CanTransferItemTo(container.GetInventory(), MyItemType.MakeOre("Iron_Ore")))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates a text string to represent a percentage bar
        /// </summary>
        /// <param name="fontsize">fontsize used on the display</param>
        /// <param name="fill">amount of the bar to fill from 0-1</param>
        /// <returns>formatted string</returns>
        private String CreateBar(float fontsize, float fill)
        {
            //[ = 9 aw
            //| = 6 aw
            int lcdwidth = 540; //screen witdth in units at font size 1

            //adjust the screen width to match the font size
            int lcdadjwidth = (int)((float)lcdwidth / fontsize);
            //work out the amount of symbols needed to fill the bar when the opening and closing brackets are taken into account
            int symbols = (lcdadjwidth - (9 * 2)) / 6;
            //calculate how many smbols should be full
            int fillcount = (int)(symbols * fill);
            //prevent bar over/under fill
            if (fillcount > symbols)
                fillcount = symbols;
            if (fillcount < 0)
                fillcount = 0;

            return($"[{new String('|', fillcount)}{new String('\'', symbols - fillcount)}]\n");
        }

    }
}
