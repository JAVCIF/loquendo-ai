// Director logic lives in LoquendoAI.Infrastructure.Director (testable without WPF, see tests/).
// "using static" keeps the existing calls (ParseDirectorPrompt, SameDirectorName…) unchanged.
global using LoquendoAI.Infrastructure.Director;
global using static LoquendoAI.Infrastructure.Director.DirectorScript;
global using static LoquendoAI.Infrastructure.Director.EpisodePlanning;
