using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Planner;
using Unity.AI.Planner.Traits;
using UnityEngine;
using Generated.AI.Planner.StateRepresentation;
using Generated.AI.Planner.StateRepresentation.CoverTactic;

namespace Generated.AI.Planner.Plans.CoverTactic
{
    public struct DefaultCumulativeRewardEstimator : ICumulativeRewardEstimator<StateData>
    {
        public BoundedValue Evaluate(StateData state)
        {
            return new BoundedValue(-100, 0, 100);
        }
    }

    public struct TerminationEvaluator : ITerminationEvaluator<StateData>
    {
        public bool IsTerminal(StateData state, out float terminalReward)
        {
            terminalReward = 0f;
            return false;
        }
    }

    class CoverTacticExecutor : BaseTraitBasedPlanExecutor<TraitBasedObject, StateEntityKey, StateData, StateDataContext, StateManager, ActionKey>
    {
        static Dictionary<Guid, string> s_ActionGuidToNameLookup = new Dictionary<Guid,string>()
        {
            { ActionScheduler.PickupWeaponGuid, nameof(PickupWeapon) },
            { ActionScheduler.TakeCoverGuid, nameof(TakeCover) },
            { ActionScheduler.SkipTurnGuid, nameof(SkipTurn) },
        };

        PlannerStateConverter<TraitBasedObject, StateEntityKey, StateData, StateDataContext, StateManager> m_StateConverter;

        public  CoverTacticExecutor(StateManager stateManager, PlannerStateConverter<TraitBasedObject, StateEntityKey, StateData, StateDataContext, StateManager> stateConverter)
        {
            m_StateManager = stateManager;
            m_StateConverter = stateConverter;
        }

        public override string GetActionName(IActionKey actionKey)
        {
            s_ActionGuidToNameLookup.TryGetValue(((IActionKeyWithGuid)actionKey).ActionGuid, out var name);
            return name;
        }

        protected override void Act(ActionKey actionKey)
        {
            var stateData = m_StateManager.GetStateData(CurrentPlanState, false);
            var actionName = string.Empty;

            switch (actionKey.ActionGuid)
            {
                case var actionGuid when actionGuid == ActionScheduler.PickupWeaponGuid:
                    actionName = nameof(PickupWeapon);
                    break;
                case var actionGuid when actionGuid == ActionScheduler.TakeCoverGuid:
                    actionName = nameof(TakeCover);
                    break;
                case var actionGuid when actionGuid == ActionScheduler.SkipTurnGuid:
                    actionName = nameof(SkipTurn);
                    break;
            }

            var executeInfos = GetExecutionInfo(actionName);
            if (executeInfos == null)
                return;

            var argumentMapping = executeInfos.GetArgumentValues();
            var arguments = new object[argumentMapping.Count()];
            var i = 0;
            foreach (var argument in argumentMapping)
            {
                var split = argument.Split('.');

                int parameterIndex = -1;
                var traitBasedObjectName = split[0];

                if (string.IsNullOrEmpty(traitBasedObjectName))
                    throw new ArgumentException($"An argument to the '{actionName}' callback on '{m_Actor?.name}' DecisionController is invalid");

                switch (actionName)
                {
                    case nameof(PickupWeapon):
                        parameterIndex = PickupWeapon.GetIndexForParameterName(traitBasedObjectName);
                        break;
                    case nameof(TakeCover):
                        parameterIndex = TakeCover.GetIndexForParameterName(traitBasedObjectName);
                        break;
                    case nameof(SkipTurn):
                        parameterIndex = SkipTurn.GetIndexForParameterName(traitBasedObjectName);
                        break;
                }

                if (parameterIndex == -1)
                    throw new ArgumentException($"Argument '{traitBasedObjectName}' to the '{actionName}' callback on '{m_Actor?.name}' DecisionController is invalid");

                var traitBasedObjectIndex = actionKey[parameterIndex];
                if (split.Length > 1) // argument is a trait
                {
                    switch (split[1])
                    {
                        case nameof(Agent):
                            var traitAgent = stateData.GetTraitOnObjectAtIndex<Agent>(traitBasedObjectIndex);
                            arguments[i] = split.Length == 3 ? traitAgent.GetField(split[2]) : traitAgent;
                            break;
                        case nameof(Location):
                            var traitLocation = stateData.GetTraitOnObjectAtIndex<Location>(traitBasedObjectIndex);
                            arguments[i] = split.Length == 3 ? traitLocation.GetField(split[2]) : traitLocation;
                            break;
                        case nameof(Moveable):
                            var traitMoveable = stateData.GetTraitOnObjectAtIndex<Moveable>(traitBasedObjectIndex);
                            arguments[i] = split.Length == 3 ? traitMoveable.GetField(split[2]) : traitMoveable;
                            break;
                        case nameof(Item):
                            var traitItem = stateData.GetTraitOnObjectAtIndex<Item>(traitBasedObjectIndex);
                            arguments[i] = split.Length == 3 ? traitItem.GetField(split[2]) : traitItem;
                            break;
                        case nameof(Cover):
                            var traitCover = stateData.GetTraitOnObjectAtIndex<Cover>(traitBasedObjectIndex);
                            arguments[i] = split.Length == 3 ? traitCover.GetField(split[2]) : traitCover;
                            break;
                    }
                }
                else // argument is an object
                {
                    var planStateId = stateData.GetTraitBasedObjectId(traitBasedObjectIndex);
                    GameObject dataSource;
                    if (m_PlanStateToGameStateIdLookup.TryGetValue(planStateId.Id, out var gameStateId))
                        dataSource = m_StateConverter.GetDataSource(new TraitBasedObjectId { Id = gameStateId });
                    else
                        dataSource = m_StateConverter.GetDataSource(planStateId);

                    Type expectedType = executeInfos.GetParameterType(i);
                    // FIXME - if this is still needed
                    // if (typeof(ITraitBasedObjectData).IsAssignableFrom(expectedType))
                    // {
                    //     arguments[i] = dataSource;
                    // }
                    // else
                    {
                        arguments[i] = null;
                        var obj = dataSource;
                        if (obj != null && obj is GameObject gameObject)
                        {
                            if (expectedType == typeof(GameObject))
                                arguments[i] = gameObject;

                            if (typeof(Component).IsAssignableFrom(expectedType))
                                arguments[i] = gameObject == null ? null : gameObject.GetComponent(expectedType);
                        }
                    }
                }

                i++;
            }

            CurrentActionKey = actionKey;
            StartAction(executeInfos, arguments);
        }

        public override ActionParameterInfo[] GetActionParametersInfo(IStateKey stateKey, IActionKey actionKey)
        {
            string[] parameterNames = {};
            var stateData = m_StateManager.GetStateData((StateEntityKey)stateKey, false);

            switch (((IActionKeyWithGuid)actionKey).ActionGuid)
            {
                 case var actionGuid when actionGuid == ActionScheduler.PickupWeaponGuid:
                    parameterNames = PickupWeapon.parameterNames;
                        break;
                 case var actionGuid when actionGuid == ActionScheduler.TakeCoverGuid:
                    parameterNames = TakeCover.parameterNames;
                        break;
                 case var actionGuid when actionGuid == ActionScheduler.SkipTurnGuid:
                    parameterNames = SkipTurn.parameterNames;
                        break;
            }

            var parameterInfo = new ActionParameterInfo[parameterNames.Length];
            for (var i = 0; i < parameterNames.Length; i++)
            {
                var traitBasedObjectId = stateData.GetTraitBasedObjectId(((ActionKey)actionKey)[i]);

#if DEBUG
                parameterInfo[i] = new ActionParameterInfo { ParameterName = parameterNames[i], TraitObjectName = traitBasedObjectId.Name.ToString(), TraitObjectId = traitBasedObjectId.Id };
#else
                parameterInfo[i] = new ActionParameterInfo { ParameterName = parameterNames[i], TraitObjectName = traitBasedObjectId.ToString(), TraitObjectId = traitBasedObjectId.Id };
#endif
            }

            return parameterInfo;
        }
    }
}
