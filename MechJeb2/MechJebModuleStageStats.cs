using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MechJeb2;
using UnityEngine;
using System.Threading;

namespace MuMech
{
    //Other modules can request that the stage stats be computed by calling RequestUpdate
    //This module will then run the stage stats computation in a separate thread, update
    //the publicly available atmoStats and vacStats. Then it will disable itself unless
    //it got another RequestUpdate in the meantime.
    public class MechJebModuleStageStats : ComputerModule
    {
        public MechJebModuleStageStats(MechJebCore core) : base(core) { }

        [ToggleInfoItem("ΔV include cosine losses", InfoItem.Category.Thrust, showInEditor = true)]
        public bool dVLinearThrust = true;

	    public FuelFlowSimulation.Stats[] atmoStats = {};
	    public FuelFlowSimulation.Stats[] vacStats = {};

		private readonly ObjectPool<FuelFlowSimulation> simulationPool = new ObjectPool<FuelFlowSimulation>(() => new FuelFlowSimulation(), 10);

        public void RequestUpdate(object controller)
        {
            users.Add(controller);
            updateRequested = true;

            if (HighLogic.LoadedSceneIsEditor) TryStartSimulation();
        }

        protected bool updateRequested = false;
        protected bool simulationRunning = false;
        protected System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

        long millisecondsBetweenSimulations;

        public override void OnModuleEnabled()
        {
            millisecondsBetweenSimulations = 0;
            stopwatch.Start();
        }

        public override void OnModuleDisabled()
        {
            stopwatch.Stop();
            stopwatch.Reset();
        }

        public override void OnFixedUpdate()
        {
            TryStartSimulation();
        }
		
        public void TryStartSimulation()
        {
            if ((HighLogic.LoadedSceneIsEditor || vessel.isActiveVessel) && !simulationRunning)
            {
                //We should be running simulations periodically, but one is not running right now. 
                //Check if enough time has passed since the last one to start a new one:
                if (stopwatch.ElapsedMilliseconds > millisecondsBetweenSimulations)
                {
                    if (updateRequested)
                    {
                        updateRequested = false;

                        stopwatch.Stop();
                        stopwatch.Reset();

                        StartSimulation();
                    }
                    else
                    {
                        users.Clear();
                    }
                }
            }
        }

        protected void StartSimulation()
        {
            simulationRunning = true;

            stopwatch.Start(); //starts a timer that times how long the simulation takes

            //Create two FuelFlowSimulations, one for vacuum and one for atmosphere
            List<Part> parts = (HighLogic.LoadedSceneIsEditor ? EditorLogic.SortedShipList : vessel.parts);
            //FuelFlowSimulation[] sims = { new FuelFlowSimulation(parts, dVLinearThrust), new FuelFlowSimulation(parts, dVLinearThrust) };

			//Acquire and initialize simulation objects
	        var atmoSim = simulationPool.Acquire();
	        var vacSim = simulationPool.Acquire();

			atmoSim.Resource.Initialize(parts, dVLinearThrust);
			vacSim.Resource.Initialize(parts, dVLinearThrust);

	        var simulationSet = new SimulationSet(atmoSim, vacSim);

            //Run the simulation in a separate thread
			ThreadPool.QueueUserWorkItem(RunSimulation, simulationSet);
            //RunSimulation(sims);
        }

	    private struct SimulationSet : IDisposable
	    {
		    public readonly PoolItem<FuelFlowSimulation> AtmoSimulation;
		    public readonly PoolItem<FuelFlowSimulation> VacSimulation;

		    public SimulationSet(PoolItem<FuelFlowSimulation> atmoSimulation, PoolItem<FuelFlowSimulation> vacSimulation)
		    {
			    AtmoSimulation = atmoSimulation;
			    VacSimulation = vacSimulation;
		    }

		    public void Dispose()
		    {
				AtmoSimulation.Resource.Dispose();
				VacSimulation.Resource.Dispose();

				AtmoSimulation.Dispose();
				VacSimulation.Dispose();
		    }
	    }

        protected void RunSimulation(object o)
        {
            try
            {
                //Run the simulation
				var sims = (SimulationSet)o;
                var newAtmoStats = sims.AtmoSimulation.Resource.SimulateAllStages(1.0f, 1.0f);
                var newVacStats = sims.VacSimulation.Resource.SimulateAllStages(1.0f, 0.0f);

                atmoStats = newAtmoStats;
                vacStats = newVacStats;

				sims.Dispose();

                //see how long the simulation took
                stopwatch.Stop();
                long millisecondsToCompletion = stopwatch.ElapsedMilliseconds;
                stopwatch.Reset();

                //set the delay before the next simulation
                millisecondsBetweenSimulations = 2 * millisecondsToCompletion;

                //start the stopwatch that will count off this delay
                stopwatch.Start();
            }
            catch (Exception e)
            {
                print("Exception on MechJebModuleStageStats.RunSimulation(): " + e.Message);
            }

            simulationRunning = false;
        }
    }
}
