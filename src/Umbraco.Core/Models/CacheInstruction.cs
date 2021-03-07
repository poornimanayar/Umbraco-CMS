using System;
using System.Runtime.Serialization;

namespace Umbraco.Cms.Core.Models
{
    /// <summary>
    /// Represents a cache instruction.
    /// </summary>
    [Serializable]
    [DataContract(IsReference = true)]
    public class CacheInstruction
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CacheInstruction"/> class.
        /// </summary>
        public CacheInstruction(int id, DateTime utcStamp, string instructions, string originIdentity, int instructionCount)
        {
            Id = id;
            UtcStamp = utcStamp;
            Instructions = instructions;
            OriginIdentity = originIdentity;
            InstructionCount = instructionCount;
        }

        /// <summary>
        /// Cache instruction Id.
        /// </summary>
        public int Id { get; private set; }

        /// <summary>
        /// Cache instruction created date.
        /// </summary>
        public DateTime UtcStamp { get; private set; }

        /// <summary>
        /// Serialized instructions.
        /// </summary>
        public string Instructions { get; private set; }

        /// <summary>
        /// Identity of server originating the instruction.
        /// </summary>
        public string OriginIdentity { get; private set; }

        /// <summary>
        /// Count of instructions.
        /// </summary>
        public int InstructionCount { get; private set; }

    }
}
