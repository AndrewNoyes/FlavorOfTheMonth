﻿using System;
using System.Collections.Generic;
using FotmServerApp.Models.Base;

namespace FotmServerApp.Models
{
    public class Team : DbEntityBase
    {
        /// <summary>
        /// Unique team Id.
        /// </summary>
        public Guid TeamID { get; set; }

        /// <summary>
        /// The mean of the team's rating change values.
        /// </summary>
        public double Mean { get; set; }

        /// <summary>
        /// List of members belonging to this team.
        /// </summary>
        [DbInsertProperty(false)]
        public List<TeamMember> Members { get; set; }

        /// <summary>
        /// You know what the fuck this does.
        /// </summary>
        public Team()
        {
            Members = new List<TeamMember>();
        }
    }
}
