namespace OpenTPW;

/// <summary>
/// Theme Park Inc's Standard.sam object (<c>0xA01A60</c>, schema <c>0x996684</c>; docs/reverse/TPI-CAREER.md §2): the layered <c>data\levels\Standard.sam</c> and the level's own, bound
/// through the generic SAM binding (<see cref="ThemeParkIncSamObject"/>). The career's promotion reads its <c>FiringLineForRank</c> table here, and past it the object's later words.
/// </summary>
// [BIN:TPI-EXE:0x41B320 Standard object] the static constructor binds the object at 0xA01A60 to the schema 0x996684 (590 records) through 0x418F60
public static class ThemeParkIncStandard
{
	/// <summary>The object's address: the promotion's table <c>0xA028F8</c> is its <c>+0xE98</c>.</summary>
	public const uint ObjectAddress = 0xA01A60;
	/// <summary>The <c>FiringLineForRank[0].Limit</c> address the promotion indexes (<c>0x74C46A</c>).</summary>
	public const uint FiringLineTableAddress = 0xA028F8;

	/// <summary>The schema table <c>0x996684</c>: kind, name, the range of a kind-6 leaf and an array's count, record by record.</summary>
	// [BIN:TPI-EXE:0x996684 Standard schema] the 590 records as the executable holds them (kinds 0..8, 10 and the end; no kind 9 or 11)
	public static IReadOnlyList<ThemeParkIncSamRecord> Schema { get; } = Records(
		""
		+ "0,,0,0,0;0,,0,0,0;5,ExitLevel,0,0,0;5,ExitLevelVar,0,0,0;6,StartingCashVarPc,0,100,0;5,SmallHappinessChange,0,0,0;5,MediumHappinessChange,0,0,0;"
		+ "5,BigHappinessChange,0,0,0;5,RideVomitDivisor,0,0,0;5,ToiletDesparate,0,0,0;5,VomitCapacity,0,0,0;5,ExcitementToCostDivisor,0,0,0;"
		+ "5,MinimumEntryFee,0,0,0;7,CheapPriceMultiplier,0,0,0;7,AveragePriceMultiplier,0,0,0;7,ExpensivePriceMultiplier,0,0,0;5,DecisionVariable1,0,0,0;"
		+ "5,DecisionVariable2,0,0,0;5,DecisionVariable3,0,0,0;5,DecisionVariable4,0,0,0;5,DecisionVariable5,0,0,0;5,DecisionVariable6,0,0,0;"
		+ "5,DecisionVariable7,0,0,0;5,DecisionVariable8,0,0,0;5,DecisionVarDistWeight,0,0,0;5,DecisionVarQueueWeight,0,0,0;5,DecisionVarExcitementWeight,0,0,0;"
		+ "5,DecisionVarThirstWeight,0,0,0;5,DecisionVarHungerWeight,0,0,0;5,DecisionVarToiletWeight,0,0,0;5,DecisionVarIllnessWeight,0,0,0;"
		+ "5,SatisfactionWeightForPhoto,0,0,0;5,DistanceWeightForPhoto,0,0,0;5,PhotoThreshold,0,0,0;6,OpinionForMuchTooHigh,0,101,0;6,OpinionForTooHigh,0,101,0;"
		+ "6,OpinionForTooLow,0,101,0;6,OpinionForAboutRight,0,101,0;6,PrankeryLikelihood,0,101,0;6,StinkbombLikelihood,0,101,0;"
		+ "6,PrankHappinessThreshold,0,100,0;6,PrankLikelihood,0,100,0;4,WhingeDuration,0,0,0;4,ViciousRumoursArea,0,0,0;4,WhingeToStaffArea,0,0,0;"
		+ "6,PcOfGoodPriceIsCheap,0,101,0;4,HappyDiffForVGood,0,0,0;4,HappyDiffForGood,0,0,0;4,HappyDiffForPoor,0,0,0;4,HappyDiffForVPoor,0,0,0;"
		+ "6,PerfectExcitementMatch,0,101,0;6,GoodExcitementMatch,0,101,0;6,OKExcitementMatch,0,101,0;5,PerfectRide,0,0,0;5,GoodRide,0,0,0;5,OKRide,0,0,0;"
		+ "4,ComplainAboutThisQuality,0,0,0;4,ComplainAboutThisPortion,0,0,0;7,CheapPrizeFactor,0,0,0;4,PercentageOfKids,0,0,0;4,PercentageOfParents,0,0,0;"
		+ "4,PercentageOfGrandparents,0,0,0;4,UmbrellaModifier,0,0,0;4,WeatherModifierFrequency,0,0,0;4,SlowestWeatherChangeResponse,0,0,0;"
		+ "4,DiscardBalloonPctModifier,0,0,0;6,ThoughtBubbleHungry,0,101,0;6,ThoughtBubbleThirsty,0,101,0;6,ThoughtBubbleNeedTheLoo,0,101,0;"
		+ "6,ThoughtBubbleSick,0,101,0;7,ThoughtBubbleMess,0,0,0;6,ThoughtBubbleHappy,0,101,0;6,ThoughtBubbleUnhappy,0,101,0;1,PeepInfo,0,0,0;2,,0,0,0;"
		+ "7,Heatwave,0,0,0;7,Rain,0,0,0;7,Storm,0,0,0;7,Snow,0,0,0;3,PeepHappinessWeatherEffects,0,0,3;2,,0,0,0;7,Heatwave,0,0,0;7,Rain,0,0,0;7,Storm,0,0,0;"
		+ "7,Snow,0,0,0;3,PeepThirstWeatherEffects,0,0,3;2,,0,0,0;7,Heatwave,0,0,0;7,Rain,0,0,0;7,Storm,0,0,0;7,Snow,0,0,0;3,PeepHungerWeatherEffects,0,0,3;"
		+ "2,,0,0,0;6,PreferredExcitement,0,100,0;5,StartingCash,0,0,0;6,BoredomThreshold,0,100,0;4,Generation,0,0,0;7,SpeedMultiplier,0,0,0;3,PeepTypes,0,0,24;"
		+ "0,,0,0,0;7,ScalingFactor,0,0,0;7,XAdjustCamera,0,0,0;7,YAdjustCamera,0,0,0;7,ZAdjustCamera,0,0,0;1,GenericUmbrellaModifiers,0,0,0;2,,0,0,0;7,X,0,0,0;"
		+ "7,Y,0,0,0;7,Z,0,0,0;7,Projection,0,0,0;7,HandPosDistance,0,0,0;7,HandPosAngle,0,0,0;7,FPX,0,0,0;7,FPY,0,0,0;7,FPZ,0,0,0;7,FPProjection,0,0,0;"
		+ "7,FPCentreOffsetX,0,0,0;7,FPCentreOffsetZ,0,0,0;7,FPHandPosDistance,0,0,0;7,FPHandPosAngle,0,0,0;3,PersonSpriteUmbrellaModifiers,0,0,12;0,,0,0,0;"
		+ "4,InitialCash,0,0,0;5,InitialAdmissionFee,0,0,0;1,BankAccountInfo,0,0,0;2,,0,0,0;5,LoanAmount,0,0,0;6,APRInPercent,0,100,0;"
		+ "5,RepaymentPeriodInMonths,0,0,0;5,Lendername,0,0,0;3,LoanInfo,0,0,10;0,,0,0,0;4,CompanySharesIssued,0,0,0;4,InitialSharesOwned,0,0,0;"
		+ "4,InitialSharePrice,0,0,0;4,AnnRptSimilarPercentage,0,0,0;4,AnnRptBigChangePercentage,0,0,0;1,ShareInfo,0,0,0;0,,0,0,0;"
		+ "6,ChanceToGetGreatHandymen,0,100,0;6,ChanceToGetGreatMechanics,0,100,0;6,ChanceToGetGreatEntertainers,0,100,0;6,ChanceToGetGreatGuards,0,100,0;"
		+ "6,ChanceToGetGreatResearchers,0,100,0;6,AvgGradeOfHandymen,0,4,0;6,AvgGradeOfMechanics,0,4,0;6,AvgGradeOfEntertainers,0,4,0;6,AvgGradeOfGuards,0,4,0;"
		+ "6,AvgGradeOfResearchers,0,4,0;4,AvgExperienceOfHandymen,0,0,0;4,AvgExperienceOfMechanics,0,0,0;4,AvgExperienceOfEntertainers,0,0,0;"
		+ "4,AvgExperienceOfGuards,0,0,0;4,AvgExperienceOfResearchers,0,0,0;5,BeginningNumberOfHandymen,0,0,0;5,BeginningNumberOfMechanics,0,0,0;"
		+ "5,BeginningNumberOfEntertainers,0,0,0;5,BeginningNumberOfGuards,0,0,0;5,BeginningNumberOfResearchers,0,0,0;5,MaxHandymen,0,0,0;5,MaxMechanics,0,0,0;"
		+ "5,MaxEntertainers,0,0,0;5,MaxGuards,0,0,0;5,MaxResearchers,0,0,0;5,MaxGardeners,0,0,0;5,MinHandymenInPool,0,0,0;5,MinMechanicsInPool,0,0,0;"
		+ "5,MinEntertainersInPool,0,0,0;5,MinGuardsInPool,0,0,0;5,MinResearchersInPool,0,0,0;5,MinGardenersInPool,0,0,0;5,MaxHandymenInPark,0,0,0;"
		+ "5,MaxMechanicsInPark,0,0,0;5,MaxEntertainersInPark,0,0,0;5,MaxGuardsInPark,0,0,0;5,MaxResearchersInPark,0,0,0;5,MaxGardenersInPark,0,0,0;"
		+ "5,BaseCostPerStaff,0,0,0;5,CostPerQualityLevel,0,0,0;5,TimeBetweenStaffUpdates,0,0,0;5,MaxNumberOfStaffPerUpdate,0,0,0;5,StaffTimeoutTime,0,0,0;"
		+ "5,MinExperience,0,0,0;5,AvgWageDifference,0,0,0;1,StaffPoolInfo,0,0,0;0,,0,0,0;5,MinPeople,0,0,0;5,TimeBetweenArrivals,0,0,0;4,FixedRate,0,0,0;"
		+ "4,NewParkBonus,0,0,0;5,PointsPerVisitor,0,0,0;4,RainPercentageModifier,0,0,0;4,StormPercentageModifier,0,0,0;4,SnowPercentageModifier,0,0,0;"
		+ "4,HeatwavePercentageModifier,0,0,0;1,Arrival,0,0,0;0,,0,0,0;6,RestLevel,0,100,0;5,HappyHitCosNoRestArea,0,0,0;5,HappyHitCosRoomInaccessible,0,0,0;"
		+ "4,MaxExperience,0,0,0;4,UnHappyWageVal,0,0,0;4,HappyWageVal,0,0,0;4,LowestWageHappiness,0,0,0;7,MinRecuperationRate,0,0,0;7,MaxRecuperationRate,0,0,0;"
		+ "7,MinHappinessRecuperationRate,0,0,0;7,MaxHappinessRecuperationRate,0,0,0;4,MinWage,0,0,0;4,MaxWage,0,0,0;4,WageStep,0,0,0;"
		+ "4,PercentageForBadWageDecision,0,0,0;4,MotivationFactor,0,0,0;4,StaffSlackingEnergy,0,0,0;4,MinDistanceForSlacking,0,0,0;4,StaffRoomCapacity,0,0,0;"
		+ "4,MaxStaffPerStaffRoom,0,0,0;4,MinStaffPerStaffRoom,0,0,0;4,MaxSpeedDifference,0,0,0;4,TrainingExtraExperience,0,0,0;4,MinExperienceForTraining,0,0,0;"
		+ "4,SkillIncrement,0,0,0;4,ProgessUpperLimit,0,0,0;1,AllStaffConstants,0,0,0;0,,0,0,0;4,MinMonthsForStrike,0,0,0;4,MinStaffOfTypeForStrike,0,0,0;"
		+ "4,EnergyPercentToStrike,0,0,0;4,HappyPercentToStrike,0,0,0;4,MotivationPercentToStrike,0,0,0;1,Strike,0,0,0;0,,0,0,0;4,MinWorkDuration,0,0,0;"
		+ "4,MaxWorkDuration,0,0,0;4,MinDetectionRange,0,0,0;4,MaxDetectionRange,0,0,0;4,MaxWage,0,0,0;1,Gardener,0,0,0;0,,0,0,0;4,MinWorkDuration,0,0,0;"
		+ "4,MaxWorkDuration,0,0,0;4,MinActiveRange,0,0,0;4,MaxActiveRange,0,0,0;4,MaxWage,0,0,0;1,Entertainer,0,0,0;0,,0,0,0;4,MinWorkDuration,0,0,0;"
		+ "4,MaxWorkDuration,0,0,0;4,MinDetectionRange,0,0,0;4,MaxDetectionRange,0,0,0;4,MaxWage,0,0,0;1,Handyman,0,0,0;0,,0,0,0;4,MinWorkDuration,0,0,0;"
		+ "4,MaxWorkDuration,0,0,0;4,MinDetectionRange,0,0,0;4,MaxDetectionRange,0,0,0;4,MaxWage,0,0,0;1,Guard,0,0,0;0,,0,0,0;4,MinWorkDuration,0,0,0;"
		+ "4,MaxWorkDuration,0,0,0;4,MinResearchAbility,0,0,0;4,MaxResearchAbility,0,0,0;4,MaxWage,0,0,0;1,Researcher,0,0,0;0,,0,0,0;4,MinWorkDuration,0,0,0;"
		+ "4,MaxWorkDuration,0,0,0;4,MaxWage,0,0,0;1,Mechanic,0,0,0;2,,0,0,0;5,PayMultiplier,0,0,0;3,PerTypeStaffConsts,0,0,5;2,,0,0,0;"
		+ "5,HappinessEffectOnCell,0,0,0;5,ExperiencePerMonth,0,0,0;3,EntertainerConstsPerGrade,0,0,5;2,,0,0,0;5,ExperiencePerMonth,0,0,0;"
		+ "3,HandymanConstsPerGrade,0,0,5;2,,0,0,0;5,ExperiencePerMonth,0,0,0;3,MechanicConstsPerGrade,0,0,5;2,,0,0,0;5,WorkDuration,0,0,0;"
		+ "5,ExperiencePerMonth,0,0,0;5,NoRRoomHappyHit,0,0,0;3,ResearcherConstsPerGrade,0,0,5;2,,0,0,0;5,ExperiencePerMonth,0,0,0;3,GuardConstsPerGrade,0,0,5;"
		+ "2,,0,0,0;4,RequiredPromotionLevel,0,0,0;4,MotivationGainModifier,0,0,0;4,MotivationLossModifier,0,0,0;4,RecuperationModifier,0,0,0;"
		+ "4,HappinessRecuperationModifier,0,0,0;4,InitialWageModifier,0,0,0;4,InitialExperienceModifier,0,0,0;4,ExperienceGainModifier,0,0,0;"
		+ "4,TrainingExperienceGainModifier,0,0,0;3,StaffAgencies,0,0,4;2,,0,0,0;6,PercentageChanceForHeatwave,-1,101,0;6,PercentageChanceForRain,-1,101,0;"
		+ "6,PercentageChanceForStorm,-1,101,0;6,PercentageChanceForSnow,-1,101,0;3,Seasons,0,0,4;0,,0,0,0;5,DaysOfWarning,0,0,0;6,DaysBetweenChanges,1,1000,0;"
		+ "4,YearsBeforeFirstFreakWeather,0,0,0;6,SpeedOfChange,1,100,0;5,MinDaysForWeatherSpell,0,0,0;5,MaxDaysForWeatherSpell,0,0,0;"
		+ "5,MinDaysForFreakWeatherSpell,0,0,0;5,MaxDaysForFreakWeatherSpell,0,0,0;1,Weather,0,0,0;0,,0,0,0;6,QualityForNormalHigh,-1,101,0;"
		+ "6,QualityForNormalLow,-1,101,0;6,QualityForHeatWaveHigh,-1,101,0;6,QualityForHeatWaveLow,-1,101,0;6,QualityForSnowLow,-1,101,0;"
		+ "6,QualityForSnowHigh,-1,101,0;6,QualityForRainLow,-1,101,0;6,QualityForRainHigh,-1,101,0;6,QualityForLightningLow,-1,101,0;"
		+ "6,QualityForLightningHigh,-1,101,0;6,LightningCountdownSpeed,1,1000,0;6,LightningCountdownFrom,1,10000,0;5,LightningTime,0,0,0;"
		+ "6,ThunderStartsThisFarAway,0,15,0;6,ThunderRandomiser,1,4,0;6,MaxRaindrops,0,1000,0;1,WeatherEffects,0,0,0;0,,0,0,0;5,MinTimeAnyMessage,0,0,0;"
		+ "5,MinTimeSameMessage,0,0,0;1,Advisor,0,0,0;2,,0,0,0;6,PercentageForThisTech,0,100,0;3,ResearchTech,0,0,10;2,,0,0,0;4,Effort,0,0,0;"
		+ "3,ResearchCategories,0,0,5;0,,0,0,0;6,StartingWorkLoad,70,101,0;8,SpeedScaleDown,0,0,0;8,CostScaleUp,0,0,0;1,Research,0,0,0;0,,0,0,0;"
		+ "4,HeightfieldXStart,0,0,0;4,HeightfieldYStart,0,0,0;4,HeightfieldWidth,0,0,0;4,HeightfieldHeight,0,0,0;4,FixedItemOriginX,0,0,0;"
		+ "4,FixedItemOriginY,0,0,0;1,MapInfo,0,0,0;0,,0,0,0;5,NumTicketBooths,0,0,0;5,BusStopAPosX,0,0,0;5,BusStopAPosY,0,0,0;5,BusStopBPosX,0,0,0;"
		+ "5,BusStopBPosY,0,0,0;5,BusStopCPosX,0,0,0;5,BusStopCPosY,0,0,0;5,EntranceAPosX,0,0,0;5,EntranceAPosY,0,0,0;5,EntranceBPosX,0,0,0;"
		+ "5,EntranceBPosY,0,0,0;5,CrossingParkSideAPosX,0,0,0;5,CrossingParkSideAPosY,0,0,0;5,CrossingParkSideBPosX,0,0,0;5,CrossingParkSideBPosY,0,0,0;"
		+ "5,CrossingBSSideAPosX,0,0,0;5,CrossingBSSideAPosY,0,0,0;5,CrossingBSSideBPosX,0,0,0;5,CrossingBSSideBPosY,0,0,0;5,StrikeAreaStartX,0,0,0;"
		+ "5,StrikeAreaStartY,0,0,0;5,StrikeAreaSizeX,0,0,0;5,StrikeAreaSizeY,0,0,0;1,FixedItemInfo,0,0,0;2,,0,0,0;5,PosX,0,0,0;5,PosY,0,0,0;5,Angle,0,0,0;"
		+ "3,TicketBooth,0,0,3;2,,0,0,0;5,PosX,0,0,0;5,PosY,0,0,0;3,KidRelease,0,0,2;0,,0,0,0;5,QueueCell,0,0,0;5,PathCell,0,0,0;5,KartTrackCell,0,0,0;"
		+ "5,WaterTrackCell,0,0,0;5,MapCell,0,0,0;1,Costs,0,0,0;2,,0,0,0;5,Radius,0,0,0;4,Happiness,0,0,0;4,Illness,0,0,0;4,Hunger,0,0,0;4,Security,0,0,0;"
		+ "4,Attraction,0,0,0;3,RegionFX,0,0,15;0,,0,0,0;4,BackColour,0,0,0;4,FogColour,0,0,0;4,AmbientLightLevel,0,0,0;4,DirectionalLightLevel,0,0,0;"
		+ "4,ProceduralTexturingXOffset,0,0,0;4,ProceduralTexturingYOffset,0,0,0;1,ThemeEngine,0,0,0;0,,0,0,0;7,X,0,0,0;7,Y,0,0,0;7,Z,0,0,0;1,LightNormal,0,0,0;"
		+ "2,,0,0,0;6,AddOns,0,35,0;6,SubOffs,0,35,0;3,ThemeAdvisorCostumes,0,0,4;0,,0,0,0;7,X,0,0,0;7,Y,0,0,0;7,Z,0,0,0;1,WaterStartPos,0,0,0;0,,0,0,0;"
		+ "5,n,0,0,0;1,NumSkyObjects,0,0,0;2,,0,0,0;7,X,0,0,0;8,Y,0,0,0;7,Z,0,0,0;8,Size,0,0,0;5,FrontFlag,0,0,0;10,Filename,0,0,0;3,SkyObjects,0,0,4;0,,0,0,0;"
		+ "6,Reverb,0,16,0;1,Sound,0,0,0;0,,0,0,0;4,Visitors,0,0,0;4,PeopleInPark,0,0,0;4,Happiness,0,0,0;5,AtLeastThisManyHappyPeople,0,0,0;4,ProfitYear,0,0,0;"
		+ "4,RecentVisitors,0,0,0;4,RecentVisitorMonths,0,0,0;1,GoldenTicketLocal,0,0,0;0,,0,0,0;4,CoasterHeight,0,0,0;4,GokartExcitement,0,0,0;"
		+ "4,WaterLength,0,0,0;4,MinCellsOwned,0,0,0;4,MinCellsCovered,0,0,0;1,GoldenTicketGlobal,0,0,0;0,,0,0,0;4,CoasterHeight,0,0,0;4,CoasterSpeed,0,0,0;"
		+ "4,Hygiene,0,0,0;4,HygieneMinParkSize,0,0,0;4,Security,0,0,0;4,Decoration,0,0,0;4,Training,0,0,0;4,TrainingMinStaff,0,0,0;4,GokartSpeed,0,0,0;"
		+ "4,WaterLength,0,0,0;4,MinCellsOwned,0,0,0;4,MinCellsCovered,0,0,0;1,ParkAward,0,0,0;0,,0,0,0;4,DaysBetweenOffers,0,0,0;"
		+ "4,MissionNumberForFirstChallenge,0,0,0;4,ShortTimeLeftWarningAt,0,0,0;1,Challenges,0,0,0;2,,0,0,0;4,Limit,0,0,0;3,FiringLineForRank,0,0,6;2,,0,0,0;"
		+ "4,ChallengeType,0,0,0;3,ChallengesInThisLevel,0,0,20;0,,0,0,0;4,YearsBeforeStarting,0,0,0;4,SpamEmailsPerYear,0,0,0;"
		+ "4,TreatJokeResponseDifferently,0,0,0;4,MaxDaysBeforeJokeResponse,0,0,0;4,MinDaysBeforeJokeResponse,0,0,0;4,JokeWeighting,0,0,0;"
		+ "4,StupidInstructionWeighting,0,0,0;4,DoorsControversyWeighting,0,0,0;4,UrbanLegendWeighting,0,0,0;4,BrainTeaserWeighting,0,0,0;1,SpamBalance,0,0,0;"
		+ "0,,0,0,0;4,Capacity,0,0,0;4,Dirty,0,0,0;4,FireRisk,0,0,0;1,LitterBin,0,0,0;0,,0,0,0;4,TimeBetweenWaterings,0,0,0;4,TestAnimation,0,0,0;"
		+ "7,HeatwaveWither,0,0,0;7,RainWither,0,0,0;7,StormWither,0,0,0;7,SnowWither,0,0,0;1,Vegetation,0,0,0;0,,0,0,0;4,Mail,0,0,0;4,CutScenes,0,0,0;"
		+ "4,TipOfTheDay,0,0,0;4,All,0,0,0;1,CutTheCrap,0,0,0;0,,0,0,0;4,OverrideGeneration,0,0,0;4,Generation,0,0,0;4,OverrideSpriteID,0,0,0;"
		+ "4,KidSpriteID,0,0,0;4,ParentSpriteID,0,0,0;4,GrandparentSpriteID,0,0,0;1,SpriteTest,0,0,0;0,,0,0,0;4,MiniMenuOpenTime,0,0,0;4,ToolTipDelay,0,0,0;"
		+ "4,ToolTipFadeInTime,0,0,0;4,ToolTipXOfs,0,0,0;4,ToolTipYOfs,0,0,0;4,ToolTipMaxXMove,0,0,0;4,ToolTipMaxYMove,0,0,0;4,ToolTipOffsetWhere,0,0,0;"
		+ "4,MouseButtonHeldTime,0,0,0;7,MouseScrollSpeedX,0,0,0;7,MouseScrollSpeedY,0,0,0;4,MenuTextR,0,0,0;4,MenuTextG,0,0,0;4,MenuTextB,0,0,0;"
		+ "4,MenuTextHighlightedR,0,0,0;4,MenuTextHighlightedG,0,0,0;4,MenuTextHighlightedB,0,0,0;4,MenuTextDisabledR,0,0,0;4,MenuTextDisabledG,0,0,0;"
		+ "4,MenuTextDisabledB,0,0,0;4,MenuTextDepressedR,0,0,0;4,MenuTextDepressedG,0,0,0;4,MenuTextDepressedB,0,0,0;4,HeatMapEmptyR,0,0,0;"
		+ "4,HeatMapEmptyG,0,0,0;4,HeatMapEmptyB,0,0,0;4,HeatMapEmptyA,0,0,0;4,HeatMapMinR,0,0,0;4,HeatMapMinG,0,0,0;4,HeatMapMinB,0,0,0;4,HeatMapMinA,0,0,0;"
		+ "4,HeatMapMidR,0,0,0;4,HeatMapMidG,0,0,0;4,HeatMapMidB,0,0,0;4,HeatMapMidA,0,0,0;4,HeatMapMaxR,0,0,0;4,HeatMapMaxG,0,0,0;4,HeatMapMaxB,0,0,0;"
		+ "4,HeatMapMaxA,0,0,0;1,UserInterface,0,0,0;2,,0,0,0;4,XOffset,0,0,0;4,YOffset,0,0,0;3,ToolTips,0,0,291;2,,0,0,0;5,CamX,0,0,0;5,CamY,0,0,0;5,CamZ,0,0,0;"
		+ "5,CamZoom,0,0,0;5,CamRot,0,0,0;3,AllThemes,0,0,15;0,,0,0,0;5,MaxX,0,0,0;5,MaxZ,0,0,0;5,MinX,0,0,0;5,MinZ,0,0,0;1,CoasterEditExtents,0,0,0;0,,0,0,0;"
		+ "5,Minutes,0,0,0;1,FirstHour,0,0,0;0,,0,0,0;5,ZoomLevel,0,0,0;1,AutoRotate,0,0,0;12,,0,0,0" );

	private static ThemeParkIncSamRecord[] Records( string text ) => text.Split( ';' ).Select( record =>
	{
		var fields = record.Split( ',' );
		return new ThemeParkIncSamRecord( (ThemeParkIncSamKind)int.Parse( fields[0], System.Globalization.CultureInfo.InvariantCulture ), fields[1],
			int.Parse( fields[2], System.Globalization.CultureInfo.InvariantCulture ), int.Parse( fields[3], System.Globalization.CultureInfo.InvariantCulture ),
			int.Parse( fields[4], System.Globalization.CultureInfo.InvariantCulture ) );
	} ).ToArray();

	/// <summary>
	/// The Standard object of <paramref name="theme"/>: the global file parsed with the reset, the level's over it (a missing level file changes nothing); a malformed line stops a file's parse with
	/// the lines before it kept, as in the original. Null when there is no global file.
	/// </summary>
	// [BIN:TPI-EXE:0x6481FD Standard parse] data\levels\Standard.sam with flag 0 (the reset), then <level>\Standard.sam with flag 1 (no reset; a missing file is not logged); the online and
	// mode-2 files of other game modes are not the slice's
	public static ThemeParkIncSamObject? Load( Func<string, byte[]?> read, string theme )
	{
		ArgumentNullException.ThrowIfNull( read );
		if ( read( "/levels/Standard.sam" ) is not { } global )
			return null;
		var standard = new ThemeParkIncSamObject( Schema );
		standard.Load( global, overlay: false );
		standard.Load( read( $"/levels/{theme}/Standard.sam" ), overlay: true );
		return standard;
	}

	/// <summary>
	/// The Standard object's words the attraction constructor writes into a new thing's script (<c>0x5DB7D5</c>): <c>LitterBin.Capacity</c>, <c>Dirty</c> and <c>FireRisk</c> (<c>+0xF30</c>,
	/// <c>+0xF34</c>, <c>+0xF38</c>: <c>0xA02990</c>..<c>0xA02998</c>) and <c>Vegetation.TimeBetweenWaterings</c> and <c>TestAnimation</c> (<c>+0xF3C</c>, <c>+0xF40</c>).
	/// </summary>
	public static ThemeParkIncConstructionWords ConstructionWords( ThemeParkIncSamObject standard )
	{
		ArgumentNullException.ThrowIfNull( standard );
		return new ThemeParkIncConstructionWords( standard[0xF30], standard[0xF34], standard[0xF38], standard[0xF3C], standard[0xF40] );
	}

	/// <summary>
	/// The word the promotion compares the firing line with for <paramref name="rank"/>: <c>0xA028F8 + 4 × rank</c> (32-bit), which for ranks 0 to 5 is <c>FiringLineForRank[rank].Limit</c>,
	/// for 6 its count dword, for 7 to 26 <c>ChallengesInThisLevel[rank - 7].ChallengeType</c> and so on through the object. Null when the address leaves the object or lands on a string
	/// leaf's pointer, whose value is a heap address.
	/// </summary>
	// [BIN:TPI-EXE:0x74C46A promotion read] the limit is [0xA028F8 + rank * 4] with no bound
	// [EXT:original-undefined-reads] a rank whose word lies outside the Standard object (other globals) or is a string pointer is not read: the promotion still sets the rank and leaves the firing line
	public static int? FiringLineLimit( ThemeParkIncSamObject standard, int rank )
	{
		ArgumentNullException.ThrowIfNull( standard );
		var offset = unchecked( FiringLineTableAddress + (uint)rank * 4 - ObjectAddress );
		if ( offset < 8 || offset >= (uint)standard.Size || standard.HoldsPointer( (int)offset ) )
			return null;
		return standard[(int)offset];
	}
}

/// <summary>The Standard words a new Theme Park Inc litter bin's and plant's script variables take at construction (<see cref="ThemeParkIncStandard.ConstructionWords"/>).</summary>
public readonly record struct ThemeParkIncConstructionWords( int BinCapacity, int BinDirty, int BinFireRisk, int TimeBetweenWaterings, int TestAnimation )
{
	/// <summary>
	/// The attraction constructor's script writes (<c>0x5DB7D5</c>..<c>0x5DB88F</c>) on a new thing of <paramref name="settings"/> through the setter <c>0x6B0780</c>: a litter bin (<c>UsageInfo.HoldsLitter</c>,
	/// thing flag 0x40) takes variables 3, 11 and 12 (the bin scripts' VAR_CAPACITY, VAR_DIRTY, VAR_FIRERISK), a plant (<c>UsageInfo.RequiresWatering</c>, flag 0x1000) variables 2, 5 and 14 (vegetation.rse's
	/// VAR_TIMEOUT, VAR_TEST, VAR_SCALE = 1000). Each write is dropped without a script or for an index outside its variables, as the setter drops it.
	/// </summary>
	// [BIN:TPI-EXE:0x5DB7D5 construction variables] test byte [thing+0x42] & 0x40 (set at 0x5DB6AE..0x5DB6C0 from the type's +0x1A8, HoldsLitter): 0x6B0780(inst, 3, [0xA02990]) (0x5DB7F1), (inst, 11, [0xA02994])
	// (0x5DB80F), (inst, 12, [0xA02998]) (0x5DB82C); test word [thing+0x42] & 0x1000 (0x5DB63F..0x5DB658, the type's +0x1AC, RequiresWatering): (inst, 2, [0xA0299C]) (0x5DB850), (inst, 5, [0xA029A0])
	// (0x5DB86E), (inst, 14, 1000) (0x5DB88A); inst = 0x6B0740(thing+0x2C), the script the constructor loaded just before (0x5DB7D0 -> 0x5DE610)
	// [BIN:TPI-EXE:0x6B0780 script variable setter] nothing without the subsystem, without an instance or for an index outside 0..+0x8C; else the instance's array (+0x1C) takes the value
	public void Apply( ObjectSettings settings, RideVM? script )
	{
		ArgumentNullException.ThrowIfNull( settings );
		if ( settings.GetBool( "UsageInfo.HoldsLitter" ) )
		{
			Set( script, 3, BinCapacity );
			Set( script, 11, BinDirty );
			Set( script, 12, BinFireRisk );
		}
		if ( settings.GetBool( "UsageInfo.RequiresWatering" ) )
		{
			Set( script, 2, TimeBetweenWaterings );
			Set( script, 5, TestAnimation );
			Set( script, 14, 1000 );
		}
	}

	private static void Set( RideVM? script, int index, int value )
	{
		if ( script != null && index >= 0 && index < script.Variables.Length )
			script.Variables[index] = value;
	}
}
