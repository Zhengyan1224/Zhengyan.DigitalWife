using Zhengyan.DigitalWife.Samples.MmdDemo;

SampleScenePaths scenePaths = SampleScenePaths.Resolve(args);

using DemoGame game = new(scenePaths);
game.Run();
