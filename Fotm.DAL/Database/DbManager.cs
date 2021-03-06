﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dapper;
using Fotm.DAL.Database.DataProvider;
using Fotm.DAL.Models.Base;
using Fotm.DAL.Util;
using WowDotNetAPI.Models;

namespace Fotm.DAL.Database
{
    /// <summary>
    /// Persistent DB class for reading and writing to the database. 
    /// </summary>
    public class DbManager : ManagerBase<DbManager>
    {
        #region Members

        private DataProviderBase _dataProvider;

        #endregion

        #region Data Provider / Connection

        /// <summary>
        /// Sets the active data provider - call this first!
        /// </summary>
        /// <param name="dataProviderType">The type of data provider <see cref="DataProviderFactory.DataProviderType"/> being set.</param>
        /// <param name="connectionProperties"></param>
        public void SetDataProvider(DataProviderFactory.DataProviderType dataProviderType, params string[] connectionProperties)
        {
            _dataProvider = DataProviderFactory.GetDataProvider(dataProviderType, connectionProperties);
        }

        #endregion

        #region CRUD

        #region Create

        #region Create Scripts

        private const string TEAM_INSERT_QUERY =
            "insert into [Team] (Bracket, MeanRatingChange, MeanRating, ModifiedDate, ModifiedStatus, ModifiedUserID) " +
            "values(@Bracket, @MeanRatingChange, @MeanRating, @ModifiedDate, @ModifiedStatus, @ModifiedUserID);" +
            "select scope_identity();";

        private const string TEAM_MEMBER_INSERT_QUERY =
            "insert into [TeamMember] " +
            "(TeamID, RatingChangeValue, CurrentRating, CharacterID, SpecID, RaceID, FactionID, GenderID,ModifiedDate, ModifiedStatus, ModifiedUserID) " +
            "values" +
            "(@TeamID, @RatingChangeValue, @CurrentRating, @CharacterID, @SpecID, @RaceID, @FactionID, @GenderID, @ModifiedDate, @ModifiedStatus, @ModifiedUserID);";

        #endregion

        /// <summary>
        /// Inserts each team and associates its team members.
        /// </summary>
        /// <param name="teams">The teams with team members to insert.</param>
        public void InsertTeamsAndMembers(IEnumerable<Team> teams)
        {
            using (var conn = _dataProvider.GetDataProviderConnection())
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        foreach (var team in teams)
                        {
                            var teamId = conn.ExecuteScalar<long>(TEAM_INSERT_QUERY, team, trans);
                            LoggingUtil.LogMessage(DateTime.Now, "Inserting Team: " + teamId, LoggingUtil.LogType.Notice);

                            foreach (var teamMember in team.TeamMembers)
                            {
                                conn.Execute(TEAM_MEMBER_INSERT_QUERY, new
                                {
                                    TeamID = teamId,
                                    RatingChangeValue = teamMember.RatingChangeValue,
                                    CurrentRating = teamMember.CurrentRating,
                                    CharacterId = teamMember.CharacterID,
                                    SpecID = teamMember.SpecID,
                                    RaceID = teamMember.RaceID,
                                    FactionID = teamMember.FactionID,
                                    GenderID = teamMember.GenderID,
                                    ModifiedDate = teamMember.ModifiedDate,
                                    ModifiedStatus = teamMember.ModifiedStatus,
                                    ModifiedUserID = teamMember.ModifiedUserID
                                }, trans);

                                LoggingUtil.LogMessage(DateTime.Now, "Inserting team member, character ID: " +
                                                          teamMember.CharacterID, LoggingUtil.LogType.Notice);
                            }
                        }

                        trans.Commit();
                    }
                    catch (Exception e)
                    {
                        trans.Rollback();
                        LoggingUtil.LogMessage(DateTime.Now, "Insert teams and members failed: " + e);
                    }
                }
            }
        }

        /// <summary>
        /// Converts an API PvpStats object to the DB Character model and inserts into DB.
        /// </summary>
        /// <param name="pvpStats">The PvpStats object retrieved from the DB.</param>
        /// <param name="region"></param>
        public void InsertCharacter(PvpStats pvpStats, WowDotNetAPI.Region region)
        {
            var realm = GetRealmByName(pvpStats.RealmName, region);
            if (realm == null)
            {
                InsertNewRealm(pvpStats.RealmName, region);
                realm = GetRealmByName(pvpStats.RealmName, region);
            }

            var spec = GetSpecByName(pvpStats.Spec);
            if (spec == null)
            {
                InsertNewSpec(pvpStats.Spec);
                spec = GetSpecByName(pvpStats.Spec);
            }

            var character = new Character
            {
                Name = pvpStats.Name,
                RealmID = realm.RealmID,
                SpecID = spec.SpecID,
                RaceID = pvpStats.RaceId,
                ClassID = pvpStats.ClassId,
                FactionID = Convert.ToBoolean(pvpStats.FactionId),
                GenderID = Convert.ToBoolean(pvpStats.GenderId),
                SeasonWins = pvpStats.SeasonWins,
                SeasonLosses = pvpStats.SeasonLosses,
                WeeklyWins = pvpStats.WeeklyWins,
                WeeklyLosses = pvpStats.WeeklyLosses,
                ModifiedDate = DateTime.Now,
                ModifiedStatus = "I",
                ModifiedUserID = 0
            };

            var columns = new[]
            {
                "Name", "RealmID", "SpecID", "RaceID",
                "ClassID", "FactionID", "GenderID",
                "SeasonWins", "SeasonLosses","WeeklyWins",
                "WeeklyLosses", "ModifiedDate",
                "ModifiedStatus", "ModifiedUserID"
            };
            var colParams = GetColumnParameters(columns);
            var query =
                      $"insert into [Character] ({string.Join(",", columns)}) " +
                      $"values ({string.Join(",", colParams)});";

            using (var conn = _dataProvider.GetDataProviderConnection())
            {
                conn.Execute(query, character);
            }
        }

        /// <summary>
        /// Uses the PvpStats (API) object to insert a new PvpStat record in the DB.
        /// </summary>
        /// <param name="pvpStats">The PvpStats object pulled from API.</param>
        /// <param name="characterId">The associated ID of the DB character record.</param>
        public void InsertPvpStats(PvpStats pvpStats, long characterId)
        {
            var cols = new[] { "Ranking ", "Rating", "CharacterID", "ModifiedDate", "ModifiedStatus", "ModifiedUserID" };
            var colParams = GetColumnParameters(cols);
            var query =
                      $"insert into [PvpStats] ({string.Join(",", cols)}) " +
                      $"values ({string.Join(",", colParams)});";
            using (var conn = _dataProvider.GetDataProviderConnection())
            {
                conn.Execute(query, new
                {
                    Ranking = pvpStats.Ranking,
                    Rating = pvpStats.Rating,
                    CharacterID = characterId,
                    ModifiedDate = DateTime.Now,
                    ModifiedStatus = "I",
                    ModifiedUserID = 0
                });
            }
        }

        /// <summary>
        /// Inserts new realm into the DB.
        /// </summary>
        /// <param name="realmName">Name of realm to insert.</param>
        /// <param name="region"></param>
        public void InsertNewRealm(string realmName, WowDotNetAPI.Region region)
        {
            var query = $"insert into [Realm] (Name, RegionID, ModifiedDate, ModifiedStatus, ModifiedUserID) " +
                        $"values (@Name, @Region,'{DateTime.Now}', 'I', 0);";
            using (var conn = _dataProvider.GetDataProviderConnection())
            {
                conn.Execute(query, new { Name = realmName, Region = region });
            }
        }

        /// <summary>
        /// Inserts new spec into DB.
        /// </summary>
        /// <param name="blizzSpecName">Name of blizz spec to insert (e.g. MAGE_FROST)</param>
        public void InsertNewSpec(string blizzSpecName)
        {
            var query = $"insert into [Spec] (SpecName, BlizzName, ModifiedDate, ModifiedStatus, ModifiedUserID) " +
                        $"values (@Name, @BlizzName, '{DateTime.Now}', 'I', 0);";
            using (var conn = _dataProvider.GetDataProviderConnection())
                conn.Execute(query, new { Name = blizzSpecName, BlizzName = blizzSpecName });
        }

        #endregion Create

        #region Read

        /// <summary>
        /// Gets the DB Character object by name and realm ID.
        /// </summary>
        /// <param name="name">Character's name.</param>
        /// <param name="realmId">Character's realm ID.</param>
        /// <returns>Character if found otherwise null.</returns>
        public Character GetCharacter(string name, int realmId)
        {
            var query = "select * from Character where Name = @Name and RealmID = @RealmID";
            using (var conn = _dataProvider.GetDataProviderConnection())
                return conn.Query<Character>(query, new { Name = name, RealmID = realmId }).FirstOrDefault();
        }

        /// <summary>
        /// Gets the DB class object by name.
        /// </summary>
        /// <param name="name">Name of the class.</param>
        /// <returns>Class if found otherwise null.</returns>
        public Class GetClassByName(string name)
        {
            var query = "select * from Class where Name = @Name;";
            using (var conn = _dataProvider.GetDataProviderConnection())
                return conn.Query<Class>(query, new { Name = name }).FirstOrDefault();
        }

        /// <summary>
        /// Gets the DB Realm object by name.
        /// </summary>
        /// <param name="name">Name of the realm.</param>
        /// <param name="region"></param>
        /// <returns>Realm if found otherwise null.</returns>
        public Realm GetRealmByName(string name, WowDotNetAPI.Region region)
        {
            var query = "select * from Realm where Name = @Name and RegionID = @RegionID;";
            using (var conn = _dataProvider.GetDataProviderConnection())
                return conn.Query<Realm>(query, new { Name = name, RegionID = region }).FirstOrDefault();
        }

        /// <summary>
        /// Gets the DB Race object by name.
        /// </summary>
        /// <param name="name">Name of the race.</param>
        /// <returns>Race if found otherwise null.</returns>
        public Race GetRaceByName(string name)
        {
            var query = "select * from Race where Name = @Name;";
            using (var conn = _dataProvider.GetDataProviderConnection())
                return conn.Query<Race>(query, new { Name = name }).FirstOrDefault();
        }

        /// <summary>
        /// Gets the DB spec object by name.
        /// </summary>
        /// <param name="name">BlizzName of the spec.</param>
        /// <returns>Spec if found otherwise null.</returns>
        public Spec GetSpecByName(string name)
        {
            var query = "select * from Spec where BlizzName = @BlizzName;";
            using (var conn = _dataProvider.GetDataProviderConnection())
                return conn.Query<Spec>(query, new { BlizzName = name }).FirstOrDefault();
        }

        /// <summary>
        /// Gets the DB Team object by name.
        /// </summary>
        /// <param name="teamId">ID of the team.</param>
        /// <returns>Team if found otherwise null.</returns>
        public Team GetTeamByTeamId(long teamId)
        {
            var query = "select * from Team where TeamID = @TeamID";
            using (var conn = _dataProvider.GetDataProviderConnection())
                return conn.Query<Team>(query, new { TeamID = teamId }).FirstOrDefault();
        }

        /// <summary>
        /// Gets the DB PvpStat object by character ID. 
        /// </summary>
        /// <param name="characterId">The associated ID of DB character record.</param>
        /// <returns>PvpStat if found otherwise null.</returns>
        public PvpStat GetPvpStatByCharacterId(long characterId)
        {
            var query = "select * from PvpStats where CharacterID = @CharacterID";
            using (var conn = _dataProvider.GetDataProviderConnection())
                return conn.Query<PvpStat>(query, new { CharacterID = characterId }).FirstOrDefault();
        }

        #endregion

        #region Update

        /// <summary>
        /// Updates the PvpStat record in DB with data from the PvpStats (pulled from the API).
        /// </summary>
        /// <param name="pvpStats">The PvpStats object pulled from API.</param>
        /// <param name="dbPvpStat">The current PvpStat (DB) object.</param>
        public void UpdatePvpStats(PvpStats pvpStats, PvpStat dbPvpStat)
        {
            var query =
                "update PvpStats " +
                "set Ranking = @Ranking, Rating = @Rating, CharacterID = @CharacterID, " +
                "ModifiedDate = @ModifiedDate, ModifiedStatus = @ModifiedStatus, ModifiedUserID = @ModifiedUserID " +
                "where PvpStatsID = @PvpStatsID;";
            using (var conn = _dataProvider.GetDataProviderConnection())
                conn.Execute(query, new
                {
                    Ranking = pvpStats.Ranking,
                    Rating = pvpStats.Rating,
                    CharacterID = dbPvpStat.CharacterID,
                    ModifiedDate = DateTime.Now,
                    ModifiedStatus = "I",
                    ModifiedUserID = 0,
                    PvpStatsID = dbPvpStat.PvpStatsID
                });
        }

        #endregion

        #region Delete

        #endregion

        #endregion

        #region Helpers 

        private string[] GetColumnNames<T>() where T : new()
        {
            var type = typeof(T);
            var properties = type.GetProperties();
            var columns = new List<string>();
            var txtInfo = new CultureInfo("en-US", false).TextInfo;

            for (var i = 0; i < properties.Length; i++)
            {
                var col = txtInfo.ToTitleCase(properties[i].Name);
                columns.Add(col);
            }

            return columns.ToArray();
        }

        private string[] GetColumnParameters(string[] columnNames)
        {
            return columnNames.Select(c => $"@{c}").ToArray();
        }

        #endregion
    }
}
