global using FootballFormation.Core.Models;
global using FootballFormation.Core.Services;
global using FootballFormation.UI.Helpers;
global using FootballFormation.UI.Navigation;
global using Microsoft.AspNetCore.Components;
global using Microsoft.Extensions.Localization;
global using MudBlazor;
// ImplicitUsings pulls in System.IO, which has its own MatchType, so the bare name is ambiguous here.
// Core needs no alias — inside FootballFormation.Core.Models the local type already wins.
global using MatchType = FootballFormation.Core.Models.MatchType;
