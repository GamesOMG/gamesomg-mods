using UnrealBuildTool;

public class GamesOMGTeleport : ModuleRules
{
	public GamesOMGTeleport(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		CppStandard = CppStandardVersion.Cpp20;

		PublicDependencyModuleNames.AddRange(new string[]
		{
			"Core", "CoreUObject", "Engine", "DeveloperSettings", "PhysicsCore", "InputCore", "EnhancedInput",
			"GeometryCollectionEngine", "AnimGraphRuntime", "AssetRegistry", "NavigationSystem", "AIModule",
			"GameplayTasks", "SlateCore", "Slate", "UMG", "RenderCore", "CinematicCamera", "Foliage", "NetCore",
			"GameplayTags", "Json", "JsonUtilities", "ApplicationCore", "Projects",
			"DummyHeaders", "FactoryGame", "SML"
		});

		if (Target.Type == TargetRules.TargetType.Editor)
		{
			PublicDependencyModuleNames.Add("AnimGraph");
		}
	}
}
