using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WaywardGamers.KParser.Parsing
{
    /// <summary>
    /// This class is for storing and querying information about parsed entities.
    /// </summary>
    internal class EntityManager
    {
        #region Singleton Construction
        /// <summary>
        /// Make the class a singleton
        /// </summary>
        private static readonly EntityManager instance = new EntityManager();

        /// <summary>
        /// Gets the singleton instance of the NewMessageManager class.
        /// </summary>
        public static EntityManager Instance { get { return instance; } }
        
        /// <summary>
        /// Private constructor ensures singleton purity.
        /// </summary>
        private EntityManager()
		{
        }
        #endregion

        #region Member variables
        Dictionary<string, EntityType> entityCollection = new Dictionary<string, EntityType>();

        internal string LastCharmedMob { get; set; }
        #endregion

        #region Initialization
        internal void Reset()
        {
            lock (entityCollection)
            {
                entityCollection.Clear();
            }

            LastCharmedMob = string.Empty;
        }
        #endregion

        #region Public methods
        /// <summary>
        /// Maintain a listing of entity types for combatants so that we
        /// don't have to continually reevaluate them at the message level.
        /// </summary>
        /// <param name="message">The message to add to the entity collection.</param>
        internal void AddEntitiesFromMessage(Message msg)
        {
            if (msg == null)
                return;

            if (msg.EventDetails == null)
                return;

            if (msg.EventDetails.CombatDetails == null)
                return;

            // Update base actor name
            string name = msg.EventDetails.CombatDetails.ActorName;

            if (name != string.Empty)
            {
                CheckAndAddEntity(name, msg.EventDetails.CombatDetails.ActorEntityType);
            }

            // Update all target names
            foreach (TargetDetails target in msg.EventDetails.CombatDetails.Targets)
            {
                name = target.Name;

                if ((name != null) && (name != string.Empty))
                {
                    CheckAndAddEntity(name, target.EntityType);
                }
            }
        }

        /// <summary>
        /// Explicitly add a charmed entity (player or mob).  This adds a _Charmed
        /// modifier to the lookup name to distinguish between charmed and
        /// uncharmed versions of the entities.
        /// </summary>
        /// <param name="name">The name of the mob that was charmed.</param>
        internal void AddCharmedPlayer(string name)
        {
            if ((name == null) || (name == string.Empty))
                return;

            string charmedName = name + "_CharmedPlayer";

            if (entityCollection.ContainsKey(charmedName) == false)
            {
                entityCollection[charmedName] = EntityType.CharmedPlayer;
            }
        }

        internal void AddCharmedMob(string name)
        {
            if ((name == null) || (name == string.Empty))
                return;

            string charmedName = name + "_CharmedMob";

            if (entityCollection.ContainsKey(charmedName) == false)
            {
                entityCollection[charmedName] = EntityType.CharmedMob;
            }
        }

        /// <summary>
        /// Check to see if we've encountered the named combatant before.
        /// If so, use the entity type we got last time.  This checks for
        /// _Pets -after- normal mob name lookup.
        /// </summary>
        /// <param name="name">The name of the combatant to look up.</param>
        /// <returns>The entity type for the name provided, if available.</returns>
        internal List<EntityType> LookupEntity(string name)
        {
            List<EntityType> entityList = new List<EntityType>();

            if (string.IsNullOrEmpty(name))
                return entityList;

            if (entityCollection.ContainsKey(name))
            {
                entityList.Add(entityCollection[name]);
            }

            if (entityCollection.ContainsKey(name + "_Pet"))
            {
                entityList.Add(EntityType.CharmedMob);
            }

            if (entityCollection.ContainsKey(name + "_CharmedPlayer"))
            {
                entityList.Add(EntityType.CharmedPlayer);
            }

            if (entityCollection.ContainsKey(name + "_CharmedMob"))
            {
                entityList.Add(EntityType.CharmedMob);
            }

            return entityList;
        }

        /// <summary>
        /// Check to see if we've encountered the named combatant before.
        /// If so, use the entity type we got last time.  This checks for
        /// _Pets -before- normal mob name lookup.
        /// </summary>
        /// <param name="name">The name of the combatant to look up.</param>
        /// <returns>The entity type for the name provided, if available.</returns>
        internal EntityType LookupCharmedEntity(string name)
        {
            if ((name == null) || (name == string.Empty))
                return EntityType.Unknown;

            if (entityCollection.ContainsKey(name + "_Pet"))
                return EntityType.Pet;

            if (entityCollection.ContainsKey(name + "_CharmedMob"))
                return EntityType.CharmedMob;

            if (entityCollection.ContainsKey(name + "_CharmedPlayer"))
                return EntityType.CharmedPlayer;

            if (entityCollection.ContainsKey(name))
                return entityCollection[name];

            return EntityType.Unknown;
        }

        internal void OverridePlayerToMob(string name)
        {
            if (entityCollection.ContainsKey(name))
            {
                if (entityCollection[name] == EntityType.Player)
                    entityCollection[name] = EntityType.Mob;
            }
        }
        #endregion

        #region Private methods
        private void CheckAndAddEntity(string name, EntityType entityType)
        {
            List<EntityType> checkEntityList = LookupEntity(name);

            // If we don't have the name in the entity list already, add it.
            if (checkEntityList.Count == 0)
            {
                if (entityType == EntityType.CharmedPlayer)
                    AddCharmedPlayer(name);
                else if (entityType == EntityType.CharmedMob)
                    AddCharmedMob(name);
                else 
                    entityCollection[name] = entityType;

                return;
            }

            // If we already have an entry of the given entity type, just return.
            if (checkEntityList.Contains(entityType))
            {
                return;
            }

            // If we have an Unknown entry in the list, replace it with the
            // value we've been given (which we already know isn't in the list).
            if (checkEntityList.Contains(EntityType.Unknown))
            {
                if (entityType == EntityType.CharmedPlayer)
                {
                    entityCollection.Remove(name);
                    AddCharmedPlayer(name);
                    return;
                }
                else if (entityType == EntityType.CharmedMob)
                {
                    entityCollection.Remove(name);
                    AddCharmedMob(name);
                    return;
                }
                else
                {
                    entityCollection[name] = entityType;
                    return;
                }
            }

            // If we're told this is a mob, but we have a player entry for the
            // given name, add this as a charmed entity.
            if (checkEntityList.Contains(EntityType.Player) && entityType == EntityType.Mob)
            {
                AddCharmedPlayer(name);
                return;
            }

            // If we're told this is a player, but we have a mob entry for the
            // given name, add a charmed entity.
            if (checkEntityList.Contains(EntityType.Mob) && entityType == EntityType.Player)
            {
                entityCollection[name] = EntityType.Player;
                AddCharmedPlayer(name);
                return;
            }

            // Anything else, add as normal.
            if (entityType == EntityType.CharmedPlayer)
                AddCharmedPlayer(name);
            else if (entityType == EntityType.CharmedMob)
                AddCharmedMob(name);
            else
                entityCollection[name] = entityType;

        }

        #endregion

        #region Snapshot
        /// <summary>
        /// Copy of the entity name/type map. Does not mutate parser state.
        /// </summary>
        internal IList<KeyValuePair<string, EntityType>> SnapshotEntities()
        {
            lock (entityCollection)
            {
                return new List<KeyValuePair<string, EntityType>>(entityCollection);
            }
        }
        #endregion

        #region Debugging output
        internal void DumpData(StreamWriter sw)
        {
            if (sw == null)
                throw new ArgumentNullException("sw");

            if (sw.BaseStream.CanWrite == false)
                throw new InvalidOperationException("Provided stream does not support writing.");

            if (entityCollection.Count > 0)
            {
                sw.WriteLine("".PadRight(42, '-'));
                sw.WriteLine("Entity List\n");
                sw.WriteLine(string.Format("{0}{1}", "Name".PadRight(32), "Type"));
                sw.WriteLine(string.Format("{0}    {1}", "".PadRight(28, '-'), "".PadRight(10, '-')));

                foreach (var entity in entityCollection)
                {
                    sw.WriteLine(string.Format("{0}{1}", entity.Key.PadRight(32), entity.Value));
                }

                sw.WriteLine("".PadRight(42, '-'));
                sw.WriteLine();
            }
            else
            {
                sw.WriteLine("".PadRight(42, '-'));
                sw.WriteLine("Entity List is Empty\n");
            }
        }
        #endregion
    }
}
