// Implicit usings pull in System.IO, which has its own MatchType (a file-globbing enum). Without
// this alias every page and dialog that names our MatchType has to spell out the full namespace,
// and the razor files would need it too. Core is unaffected — inside FootballFormation.Core.Models
// the local type already wins.
global using MatchType = FootballFormation.Core.Models.MatchType;
