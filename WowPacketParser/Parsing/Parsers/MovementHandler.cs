using System;
using System.Collections.Concurrent;
using System.IO;
using WowPacketParser.Enums;
using WowPacketParser.Enums.Version;
using WowPacketParser.Misc;
using WowPacketParser.Store;
using WowPacketParser.Store.Objects;

namespace WowPacketParser.Parsing.Parsers
{
    public static class MovementHandler
    {
        [ThreadStatic]
        public static uint CurrentMapId;

        public static int CurrentPhaseMask = 1;

        public static readonly ConcurrentBag<ushort> ActivePhases = new ConcurrentBag<ushort>();

        public static MovementInfo ReadMovementInfo(Packet packet, WowGuid guid, object index = null)
        {
            if (ClientVersion.Build == ClientVersionBuild.V4_2_0_14333)
                return ReadMovementInfo420(packet, index);

            return ReadMovementInfoGen(packet, guid, index);
        }

        private static MovementInfo ReadMovementInfoGen(Packet packet, WowGuid guid, object index)
        {
            var info = new MovementInfo
            {
                Flags = packet.Translator.ReadInt32E<MovementFlag>("Movement Flags", index)
            };

            if (ClientVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
                info.FlagsExtra = packet.Translator.ReadInt16E<MovementFlagExtra>("Extra Movement Flags", index);
            else
                info.FlagsExtra = packet.Translator.ReadByteE<MovementFlagExtra>("Extra Movement Flags", index);

            if (ClientVersion.AddedInVersion(ClientVersionBuild.V4_2_2_14545))
                if (packet.Translator.ReadGuid("Guid 2", index) != guid)
                    throw new InvalidDataException("Guids are not equal.");

            packet.Translator.ReadUInt32("Time", index);

            info.Position = packet.Translator.ReadVector3("Position", index);
            info.Orientation = packet.Translator.ReadSingle("Orientation", index);

            if (info.Flags.HasAnyFlag(MovementFlag.OnTransport))
            {
                if (ClientVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767))
                    info.TransportGuid = packet.Translator.ReadPackedGuid("Transport GUID", index);
                else
                    info.TransportGuid = packet.Translator.ReadGuid("Transport GUID", index);

                info.TransportOffset = packet.Translator.ReadVector4("Transport Position", index);
                packet.Translator.ReadInt32("Transport Time", index);

                if (ClientVersion.AddedInVersion(ClientType.WrathOfTheLichKing))
                    packet.Translator.ReadByte("Transport Seat", index);

                if (info.FlagsExtra.HasAnyFlag(MovementFlagExtra.InterpolateMove))
                    packet.Translator.ReadInt32("Transport Time", index);
            }

            if (info.Flags.HasAnyFlag(MovementFlag.Swimming | MovementFlag.Flying) ||
                info.FlagsExtra.HasAnyFlag(MovementFlagExtra.AlwaysAllowPitching))
                packet.Translator.ReadSingle("Swim Pitch", index);

            if (ClientVersion.AddedInVersion(ClientType.Cataclysm))
            {
                if (info.FlagsExtra.HasAnyFlag(MovementFlagExtra.InterpolateTurning))
                {
                    packet.Translator.ReadInt32("Fall Time", index);
                    packet.Translator.ReadSingle("Fall Velocity", index);

                    if (info.Flags.HasAnyFlag(MovementFlag.Falling))
                    {
                        packet.Translator.ReadSingle("Fall Sin Angle", index);
                        packet.Translator.ReadSingle("Fall Cos Angle", index);
                        packet.Translator.ReadSingle("Fall Speed", index);
                    }
                }
            }
            else
            {
                packet.Translator.ReadInt32("Fall Time", index);
                if (info.Flags.HasAnyFlag(MovementFlag.Falling))
                {
                    packet.Translator.ReadSingle("Fall Velocity", index);
                    packet.Translator.ReadSingle("Fall Sin Angle", index);
                    packet.Translator.ReadSingle("Fall Cos Angle", index);
                    packet.Translator.ReadSingle("Fall Speed", index);
                }
            }

            // HACK: "generic" movement flags are wrong for 4.2.2
            if (info.Flags.HasAnyFlag(MovementFlag.SplineElevation) && ClientVersion.Build != ClientVersionBuild.V4_2_2_14545)
                packet.Translator.ReadSingle("Spline Elevation", index);

            return info;
        }

        private static MovementInfo ReadMovementInfo420(Packet packet, object index)
        {
            var info = new MovementInfo
            {
                Flags = packet.Translator.ReadBitsE<MovementFlag>("Movement Flags", 30, index)
            };

            packet.Translator.ReadBitsE<MovementFlagExtra>("Extra Movement Flags", 12, index);

            var onTransport = packet.Translator.ReadBit("OnTransport", index);
            var hasInterpolatedMovement = false;
            var time3 = false;
            if (onTransport)
            {
                hasInterpolatedMovement = packet.Translator.ReadBit("HasInterpolatedMovement", index);
                time3 = packet.Translator.ReadBit("Time3", index);
            }

            var swimming = packet.Translator.ReadBit("Swimming", index);
            var interPolatedTurning = packet.Translator.ReadBit("InterPolatedTurning", index);

            var jumping = false;
            if (interPolatedTurning)
                jumping = packet.Translator.ReadBit("Jumping", index);

            var splineElevation = packet.Translator.ReadBit("SplineElevation", index);

            info.HasSplineData = packet.Translator.ReadBit("HasSplineData", index);

            packet.Translator.ResetBitReader(); // reset bitreader

            packet.Translator.ReadGuid("GUID 2", index);

            packet.Translator.ReadUInt32("Time", index);

            info.Position = packet.Translator.ReadVector3("Position", index);
            info.Orientation = packet.Translator.ReadSingle("Orientation", index);

            if (onTransport)
            {
                info.TransportGuid = packet.Translator.ReadGuid("Transport GUID", index);
                info.TransportOffset = packet.Translator.ReadVector4("Transport Position", index);
                packet.Translator.ReadByte("Transport Seat", index);
                packet.Translator.ReadInt32("Transport Time", index);
                if (hasInterpolatedMovement)
                    packet.Translator.ReadInt32("Transport Time 2", index);
                if (time3)
                    packet.Translator.ReadInt32("Transport Time 3", index);
            }
            if (swimming)
                packet.Translator.ReadSingle("Swim Pitch", index);

            if (interPolatedTurning)
            {
                packet.Translator.ReadInt32("Time Fallen", index);
                packet.Translator.ReadSingle("Fall Start Velocity", index);
                if (jumping)
                {
                    packet.Translator.ReadSingle("Jump Sin", index);
                    packet.Translator.ReadSingle("Jump Cos", index);
                    packet.Translator.ReadSingle("Jump Velocity", index);

                }
            }
            if (splineElevation)
                packet.Translator.ReadSingle("Spline Elevation", index);

            return info;
        }

        [Parser(Opcode.SMSG_ON_MONSTER_MOVE)]
        [Parser(Opcode.SMSG_MONSTER_MOVE_TRANSPORT)]
        public static void HandleMonsterMove(Packet packet)
        {
            WowGuid guid = packet.Translator.ReadPackedGuid("GUID");

            if (Storage.Objects != null && Storage.Objects.ContainsKey(guid))
            {
                WoWObject obj = Storage.Objects[guid].Item1;
                UpdateField uf;
                if (obj.UpdateFields != null && obj.UpdateFields.TryGetValue(UpdateFields.GetUpdateField(UnitField.UNIT_FIELD_FLAGS), out uf))
                    if ((uf.UInt32Value & (uint)UnitFlags.IsInCombat) == 0) // movement could be because of aggro so ignore that
                        obj.Movement.HasWpsOrRandMov = true;
            }

            if (packet.Opcode == Opcodes.GetOpcode(Opcode.SMSG_MONSTER_MOVE_TRANSPORT, Direction.ServerToClient))
            {
                WowGuid transportGuid = packet.Translator.ReadPackedGuid("Transport GUID");

                int seat = -1;
                if (ClientVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767)) // no idea when this was added exactly
                    seat = packet.Translator.ReadByte("Transport Seat");

                if (transportGuid.HasEntry() && transportGuid.GetHighType() == HighGuidType.Vehicle &&
                    guid.HasEntry() && guid.GetHighType() == HighGuidType.Creature)
                {
                    VehicleTemplateAccessory vehicleAccessory = new VehicleTemplateAccessory
                    {
                        Entry = transportGuid.GetEntry(),
                        AccessoryEntry = guid.GetEntry(),
                        SeatId = seat
                    };
                    Storage.VehicleTemplateAccessories.Add(vehicleAccessory, packet.TimeSpan);
                }
            }

            if (ClientVersion.AddedInVersion(ClientVersionBuild.V3_1_0_9767)) // no idea when this was added exactly
                packet.Translator.ReadBool("Toggle AnimTierInTrans");

            var pos = packet.Translator.ReadVector3("Position");

            packet.Translator.ReadInt32("Move Ticks");

            var type = packet.Translator.ReadByteE<SplineType>("Spline Type");

            switch (type)
            {
                case SplineType.FacingSpot:
                {
                    packet.Translator.ReadVector3("Facing Spot");
                    break;
                }
                case SplineType.FacingTarget:
                {
                    packet.Translator.ReadGuid("Facing GUID");
                    break;
                }
                case SplineType.FacingAngle:
                {
                    packet.Translator.ReadSingle("Facing Angle");
                    break;
                }
                case SplineType.Stop:
                    return;
            }

            if (ClientVersion.AddedInVersion(ClientVersionBuild.V5_1_0_16309))
            {
                // Not the best way
                ReadSplineMovement510(packet, pos);
                return;
            }

            if (ClientVersion.AddedInVersion(ClientVersionBuild.V4_2_2_14545))
            {
                // Not the best way
                ReadSplineMovement422(packet, pos);
                return;
            }

            var flags = packet.Translator.ReadInt32E<SplineFlag>("Spline Flags");

            if (flags.HasAnyFlag(SplineFlag.AnimationTier))
            {
                packet.Translator.ReadByteE<MovementAnimationState>("Animation State");
                packet.Translator.ReadInt32("Async-time in ms");
            }

            packet.Translator.ReadInt32("Move Time");

            if (flags.HasAnyFlag(SplineFlag.Trajectory))
            {
                packet.Translator.ReadSingle("Vertical Speed");
                packet.Translator.ReadInt32("Async-time in ms");
            }

            var waypoints = packet.Translator.ReadInt32("Waypoints");

            if (flags.HasAnyFlag(SplineFlag.Flying | SplineFlag.CatmullRom))
            {
                for (var i = 0; i < waypoints; i++)
                    packet.Translator.ReadVector3("Waypoint", i);
            }
            else
            {
                var newpos = packet.Translator.ReadVector3("Waypoint Endpoint");

                Vector3 mid = (pos + newpos) * 0.5f;

                for (var i = 1; i < waypoints; i++)
                {
                    var vec = packet.Translator.ReadPackedVector3();
                    vec = mid - vec;
                    packet.AddValue("Waypoint", vec, i);
                }
            }
        }

        private static void ReadSplineMovement510(Packet packet, Vector3 pos)
        {
            var flags = packet.Translator.ReadInt32E<SplineFlag434>("Spline Flags");

            if (flags.HasAnyFlag(SplineFlag434.Animation))
            {
                packet.Translator.ReadByteE<MovementAnimationState>("Animation State");
                packet.Translator.ReadInt32("Asynctime in ms"); // Async-time in ms
            }

            packet.Translator.ReadInt32("Move Time");

            if (flags.HasAnyFlag(SplineFlag434.Parabolic))
            {
                packet.Translator.ReadSingle("Vertical Speed");
                packet.Translator.ReadInt32("Async-time in ms");
            }

            var waypoints = packet.Translator.ReadInt32("Waypoints");

            if (flags.HasAnyFlag(SplineFlag434.UncompressedPath))
            {
                for (var i = 0; i < waypoints; i++)
                    packet.Translator.ReadVector3("Waypoint", i);
            }
            else
            {
                var newpos = packet.Translator.ReadVector3("Waypoint Endpoint");

                var mid = new Vector3
                {
                    X = (pos.X + newpos.X)*0.5f,
                    Y = (pos.Y + newpos.Y)*0.5f,
                    Z = (pos.Z + newpos.Z)*0.5f
                };

                if (waypoints != 1)
                {
                    var vec = packet.Translator.ReadPackedVector3();
                    vec.X += mid.X;
                    vec.Y += mid.Y;
                    vec.Z += mid.Z;
                    packet.AddValue("Waypoint: ", vec, 0);

                    if (waypoints > 2)
                    {
                        for (var i = 1; i < waypoints - 1; ++i)
                        {
                            vec = packet.Translator.ReadPackedVector3();
                            vec.X += mid.X;
                            vec.Y += mid.Y;
                            vec.Z += mid.Z;

                            packet.AddValue("Waypoint", vec, i);
                        }
                    }
                }
            }

            var unkLoopCounter = packet.Translator.ReadUInt16("Unk UInt16");
            if (unkLoopCounter > 1)
            {
                packet.Translator.ReadSingle("Unk Float 1");
                packet.Translator.ReadUInt16("Unk UInt16 1");
                packet.Translator.ReadUInt16("Unk UInt16 2");
                packet.Translator.ReadSingle("Unk Float 2");
                packet.Translator.ReadUInt16("Unk UInt16 3");

                for (var i = 0; i < unkLoopCounter; i++)
                {
                    packet.Translator.ReadUInt16("Unk UInt16 1", i);
                    packet.Translator.ReadUInt16("Unk UInt16 2", i);
                }
            }
        }

        private static void ReadSplineMovement422(Packet packet, Vector3 pos)
        {
            var flags = packet.Translator.ReadInt32E<SplineFlag422>("Spline Flags");

            if (flags.HasAnyFlag(SplineFlag422.AnimationTier))
            {
                packet.Translator.ReadByteE<MovementAnimationState>("Animation State");
                packet.Translator.ReadInt32("Asynctime in ms"); // Async-time in ms
            }

            packet.Translator.ReadInt32("Move Time");

            if (flags.HasAnyFlag(SplineFlag422.Trajectory))
            {
                packet.Translator.ReadSingle("Vertical Speed");
                packet.Translator.ReadInt32("Unk Int32 2");
            }

            var waypoints = packet.Translator.ReadInt32("Waypoints");

            if (flags.HasAnyFlag(SplineFlag422.UsePathSmoothing))
            {
                for (var i = 0; i < waypoints; i++)
                    packet.Translator.ReadVector3("Waypoint", i);
            }
            else
            {
                var newpos = packet.Translator.ReadVector3("Waypoint Endpoint");

                var mid = new Vector3
                {
                    X = (pos.X + newpos.X)*0.5f,
                    Y = (pos.Y + newpos.Y)*0.5f,
                    Z = (pos.Z + newpos.Z)*0.5f
                };

                for (var i = 1; i < waypoints; i++)
                {
                    var vec = packet.Translator.ReadPackedVector3();
                    vec.X += mid.X;
                    vec.Y += mid.Y;
                    vec.Z += mid.Z;

                    packet.AddValue("Waypoint", vec);
                }
            }
        }

        [HasSniffData]
        [Parser(Opcode.SMSG_NEW_WORLD, ClientVersionBuild.Zero, ClientVersionBuild.V4_2_2_14545)]
        [Parser(Opcode.SMSG_LOGIN_VERIFY_WORLD)]
        public static void HandleEnterWorld(Packet packet)
        {
            CurrentMapId = (uint) packet.Translator.ReadInt32<MapId>("Map ID");
            packet.Translator.ReadVector4("Position");

            packet.AddSniffData(StoreNameType.Map, (int) CurrentMapId, "NEW_WORLD");
        }

        [HasSniffData]
        [Parser(Opcode.SMSG_NEW_WORLD, ClientVersionBuild.V4_2_2_14545, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleNewWorld422(Packet packet)
        {
            packet.Translator.ReadVector3("Position");
            CurrentMapId = (uint) packet.Translator.ReadInt32<MapId>("Map");
            packet.Translator.ReadSingle("Orientation");

            packet.AddSniffData(StoreNameType.Map, (int)CurrentMapId, "NEW_WORLD");
        }

        [HasSniffData]
        [Parser(Opcode.SMSG_NEW_WORLD, ClientVersionBuild.V5_1_0_16309)]
        public static void HandleNewWorld510(Packet packet)
        {
            CurrentMapId = (uint)packet.Translator.ReadInt32<MapId>("Map");
            packet.Translator.ReadSingle("Y");
            packet.Translator.ReadSingle("Orientation");
            packet.Translator.ReadSingle("X");
            packet.Translator.ReadSingle("Z");

            packet.AddSniffData(StoreNameType.Map, (int)CurrentMapId, "NEW_WORLD");
        }

        [Parser(Opcode.SMSG_LOGIN_SET_TIME_SPEED)]
        public static void HandleLoginSetTimeSpeed(Packet packet)
        {
            packet.Translator.ReadPackedTime("Game Time");
            packet.Translator.ReadSingle("Game Speed");

            if (ClientVersion.AddedInVersion(ClientVersionBuild.V3_1_2_9901))
                packet.Translator.ReadInt32("Unk Int32");
        }

        [Parser(Opcode.SMSG_BIND_POINT_UPDATE)]
        public static void HandleBindPointUpdate(Packet packet)
        {
            packet.Translator.ReadVector3("Position");
            packet.Translator.ReadInt32<MapId>("Map Id");
            packet.Translator.ReadInt32<ZoneId>("Zone Id");
        }

        [Parser(Opcode.CMSG_UPDATE_MISSILE_TRAJECTORY)]
        public static void HandleUpdateMissileTrajectory(Packet packet)
        {
            packet.Translator.ReadGuid("GUID");
            packet.Translator.ReadInt32<SpellId>("Spell ID");
            packet.Translator.ReadSingle("Elevation");
            packet.Translator.ReadSingle("Missile speed");
            packet.Translator.ReadVector3("Current Position");
            packet.Translator.ReadVector3("Targeted Position");

            // Boolean if it will send MSG_MOVE_STOP
            if (!packet.ReadBoolean())
                return;

            var opcode = packet.Translator.ReadInt32();
            // None length is recieved, so we have to calculate the remaining bytes.
            var remainingLength = packet.Length - packet.Position;
            var bytes = packet.Translator.ReadBytes((int)remainingLength);

            using (var newpacket = new Packet(bytes, opcode, packet.Time, packet.Direction, packet.Number, packet.Formatter, packet.FileName))
                Handler.Parse(newpacket, true);
        }

        [Parser(Opcode.MSG_MOVE_TELEPORT_ACK, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleTeleportAck(Packet packet)
        {
            var guid = packet.Translator.ReadPackedGuid("Guid");
            packet.Translator.ReadInt32("Movement Counter");

            if (packet.Direction == Direction.ServerToClient)
            {
                ReadMovementInfo(packet, guid);
            }
            else
            {
                packet.Translator.ReadUInt32("Time");
            }
        }

        [Parser(Opcode.MSG_MOVE_HEARTBEAT, ClientVersionBuild.V4_2_2_14545, ClientVersionBuild.V4_3_0_15005)]
        public static void HandleMovementHeartbeat422(Packet packet)
        {
            packet.Translator.ReadBitsE<MovementFlag>("Movement flags", 30);

            packet.Translator.ReadBit("HasSplineData");

            var guidBytes = new byte[8];
            guidBytes[0] = packet.Translator.ReadBit();
            guidBytes[6] = packet.Translator.ReadBit();
            guidBytes[1] = packet.Translator.ReadBit();
            guidBytes[7] = packet.Translator.ReadBit();
            guidBytes[2] = packet.Translator.ReadBit();
            guidBytes[4] = packet.Translator.ReadBit();
            guidBytes[3] = packet.Translator.ReadBit();

            packet.Translator.ReadBitsE<MovementFlagExtra>("Movement flags extra", 12);

            guidBytes[5] = packet.Translator.ReadBit();
            var splineElevation = packet.Translator.ReadBit("SplineElevation"); // OR Swimming
            var onTransport = packet.Translator.ReadBit("OnTransport");

            var transportBytes = new byte[8];
            var hasInterpolatedMovement = false;
            var time3 = false;
            if (onTransport)
            {
                transportBytes = packet.Translator.StartBitStream(0, 6, 2, 5, 4, 1, 3, 7);
                hasInterpolatedMovement = packet.Translator.ReadBit("HasInterpolatedMovement");
                time3 = packet.Translator.ReadBit("Time3");
            }

            var swimming = packet.Translator.ReadBit("Swimming");  // OR SplineElevation
            var interPolatedTurning = packet.Translator.ReadBit("InterPolatedTurning");
            var jumping = false;
            if (interPolatedTurning)
                jumping = packet.Translator.ReadBit("Jumping");

            packet.Translator.ReadUInt32("Time");
            packet.Translator.ReadVector4("Position");

            packet.Translator.ReadXORByte(guidBytes, 7);
            packet.Translator.ReadXORByte(guidBytes, 5);

            if (splineElevation)
                packet.Translator.ReadSingle("Spline Elevation");

            packet.Translator.ReadXORByte(guidBytes, 1);
            packet.Translator.ReadXORByte(guidBytes, 6);
            packet.Translator.ReadXORByte(guidBytes, 4);
            packet.Translator.ReadXORByte(guidBytes, 3);

            if (onTransport)
            {
                packet.Translator.ReadByte("Transport Seat");
                packet.Translator.ReadSingle("Transport Orientation");
                packet.Translator.ReadVector3("Transport Position");
                packet.Translator.ReadInt32("Transport Time");
                if (hasInterpolatedMovement)
                    packet.Translator.ReadInt32("Transport Time 2");

                packet.Translator.ReadXORByte(transportBytes, 3);
                packet.Translator.ReadXORByte(transportBytes, 6);

                if (time3)
                    packet.Translator.ReadInt32("Transport Time 3");

                packet.Translator.ReadXORByte(transportBytes, 7);
                packet.Translator.ReadXORByte(transportBytes, 5);
                packet.Translator.ReadXORByte(transportBytes, 2);
                packet.Translator.ReadXORByte(transportBytes, 1);
                packet.Translator.ReadXORByte(transportBytes, 0);
                packet.Translator.ReadXORByte(transportBytes, 4);

                packet.Translator.WriteGuid("Transport Guid", transportBytes);
            }

            if (swimming)
                packet.Translator.ReadSingle("Swim Pitch");

            if (interPolatedTurning)
            {
                packet.Translator.ReadInt32("Time Fallen");
                packet.Translator.ReadSingle("Fall Start Velocity");
                if (jumping)
                {
                    packet.Translator.ReadSingle("Jump Velocity");
                    packet.Translator.ReadSingle("Jump Cos");
                    packet.Translator.ReadSingle("Jump Sin");

                }
            }

            packet.Translator.ReadXORByte(guidBytes, 2);
            packet.Translator.ReadXORByte(guidBytes, 0);

            packet.Translator.WriteGuid("Guid", guidBytes);
        }

        [Parser(Opcode.MSG_MOVE_HEARTBEAT, ClientVersionBuild.V4_3_3_15354, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleMovementHeartbeat433(Packet packet)
        {
            packet.Translator.ReadBitsE<MovementFlag>("Movement flags", 30);

            packet.Translator.ReadBit("HasSplineData");

            var guidBytes = new byte[8];
            guidBytes[0] = packet.Translator.ReadBit();
            guidBytes[6] = packet.Translator.ReadBit();
            guidBytes[1] = packet.Translator.ReadBit();
            guidBytes[7] = packet.Translator.ReadBit();
            guidBytes[2] = packet.Translator.ReadBit();
            guidBytes[4] = packet.Translator.ReadBit();
            guidBytes[3] = packet.Translator.ReadBit();

            packet.Translator.ReadBitsE<MovementFlagExtra>("Movement flags extra", 12);

            guidBytes[5] = packet.Translator.ReadBit();
            var splineElevation = packet.Translator.ReadBit("SplineElevation"); // OR Swimming
            var onTransport = packet.Translator.ReadBit("OnTransport");

            var transportBytes = new byte[8];
            var hasInterpolatedMovement = false;
            var time3 = false;
            if (onTransport)
            {
                transportBytes = packet.Translator.StartBitStream(0, 6, 2, 5, 4, 1, 3, 7);
                hasInterpolatedMovement = packet.Translator.ReadBit("HasInterpolatedMovement");
                time3 = packet.Translator.ReadBit("Time3");
            }

            var swimming = packet.Translator.ReadBit("Swimming");  // OR SplineElevation
            var interPolatedTurning = packet.Translator.ReadBit("InterPolatedTurning");
            var jumping = false;
            if (interPolatedTurning)
                jumping = packet.Translator.ReadBit("Jumping");

            packet.Translator.ReadUInt32("Time");
            packet.Translator.ReadVector4("Position");

            packet.Translator.ReadXORByte(guidBytes, 7);
            packet.Translator.ReadXORByte(guidBytes, 5);

            if (splineElevation)
                packet.Translator.ReadSingle("Spline Elevation");

            packet.Translator.ReadXORByte(guidBytes, 1);
            packet.Translator.ReadXORByte(guidBytes, 6);
            packet.Translator.ReadXORByte(guidBytes, 4);
            packet.Translator.ReadXORByte(guidBytes, 3);

            if (onTransport)
            {
                packet.Translator.ReadByte("Transport Seat");
                packet.Translator.ReadSingle("Transport Orientation");
                packet.Translator.ReadVector3("Transport Position");
                packet.Translator.ReadInt32("Transport Time");
                if (hasInterpolatedMovement)
                    packet.Translator.ReadInt32("Transport Time 2");

                packet.Translator.ReadXORByte(transportBytes, 3);
                packet.Translator.ReadXORByte(transportBytes, 6);

                if (time3)
                    packet.Translator.ReadInt32("Transport Time 3");

                packet.Translator.ReadXORByte(transportBytes, 7);
                packet.Translator.ReadXORByte(transportBytes, 5);
                packet.Translator.ReadXORByte(transportBytes, 2);
                packet.Translator.ReadXORByte(transportBytes, 1);
                packet.Translator.ReadXORByte(transportBytes, 0);
                packet.Translator.ReadXORByte(transportBytes, 4);

                packet.Translator.WriteGuid("Transport Guid", transportBytes);
            }

            if (swimming)
                packet.Translator.ReadSingle("Swim Pitch");

            if (interPolatedTurning)
            {
                packet.Translator.ReadInt32("Time Fallen");
                packet.Translator.ReadSingle("Fall Start Velocity");
                if (jumping)
                {
                    packet.Translator.ReadSingle("Jump Velocity");
                    packet.Translator.ReadSingle("Jump Cos");
                    packet.Translator.ReadSingle("Jump Sin");

                }
            }

            packet.Translator.ReadXORByte(guidBytes, 2);

            packet.Translator.ReadXORByte(guidBytes, 0);

            packet.Translator.WriteGuid("Guid", guidBytes);
        }

        [Parser(Opcode.MSG_MOVE_SET_PITCH, ClientVersionBuild.V4_2_2_14545, ClientVersionBuild.V4_3_0_15005)]
        public static void HandleMovementSetPitch422(Packet packet)
        {
            var guidBytes = new byte[8];
            guidBytes[1] = packet.Translator.ReadBit();
            guidBytes[6] = packet.Translator.ReadBit();
            guidBytes[7] = packet.Translator.ReadBit();
            guidBytes[3] = packet.Translator.ReadBit();

            packet.Translator.ReadBitsE<MovementFlag>("Movement flags", 30);

            guidBytes[5] = packet.Translator.ReadBit();
            guidBytes[2] = packet.Translator.ReadBit();
            guidBytes[0] = packet.Translator.ReadBit();

            packet.Translator.ReadBit("HasSplineData");

            guidBytes[4] = packet.Translator.ReadBit();

            packet.Translator.ReadBitsE<MovementFlagExtra>("Movement flags extra", 12);

            var splineElevation = packet.Translator.ReadBit("SplineElevation"); // OR Swimming
            var onTransport = packet.Translator.ReadBit("OnTransport");

            var transportBytes = new byte[8];
            var hasInterpolatedMovement = false;
            var time3 = false;
            if (onTransport)
            {
                transportBytes = packet.Translator.StartBitStream(0, 6, 2, 5, 4, 1, 3, 7);
                hasInterpolatedMovement = packet.Translator.ReadBit("HasInterpolatedMovement");
                time3 = packet.Translator.ReadBit("HasTime3");
            }

            var swimming = packet.Translator.ReadBit("HasPitch");  // OR SplineElevation
            var interPolatedTurning = packet.Translator.ReadBit("HasFallData");
            var jumping = false;
            if (interPolatedTurning)
                jumping = packet.Translator.ReadBit("HasFallDirection");

            packet.Translator.ReadVector3("Position");
            packet.Translator.ReadUInt32("Time");
            packet.Translator.ReadSingle("Orientation");

            packet.Translator.ReadXORByte(guidBytes, 1);
            packet.Translator.ReadXORByte(guidBytes, 4);

            if (splineElevation)
                packet.Translator.ReadSingle("Spline Elevation");

            if (onTransport)
            {
                packet.Translator.ReadByte("Transport Seat");
                packet.Translator.ReadSingle("Transport Orientation");
                packet.Translator.ReadVector3("Transport Position");
                packet.Translator.ReadInt32("Transport Time");
                if (hasInterpolatedMovement)
                    packet.Translator.ReadInt32("Transport Time 2");

                packet.Translator.ReadXORByte(transportBytes, 3);
                packet.Translator.ReadXORByte(transportBytes, 6);

                if (time3)
                    packet.Translator.ReadInt32("Transport Time 3");

                packet.Translator.ReadXORByte(transportBytes, 7);
                packet.Translator.ReadXORByte(transportBytes, 5);
                packet.Translator.ReadXORByte(transportBytes, 2);
                packet.Translator.ReadXORByte(transportBytes, 1);
                packet.Translator.ReadXORByte(transportBytes, 0);
                packet.Translator.ReadXORByte(transportBytes, 4);

                packet.Translator.WriteGuid("Transport Guid", transportBytes);
            }

            if (swimming)
                packet.Translator.ReadSingle("Swim Pitch");

            packet.Translator.ReadXORByte(guidBytes, 5);

            if (interPolatedTurning)
            {
                packet.Translator.ReadInt32("Time Fallen");
                packet.Translator.ReadSingle("Fall Start Velocity");
                if (jumping)
                {
                    packet.Translator.ReadSingle("Jump Velocity");
                    packet.Translator.ReadSingle("Jump Sin");
                    packet.Translator.ReadSingle("Jump Cos");

                }
            }

            packet.Translator.ReadXORByte(guidBytes, 0);
            packet.Translator.ReadXORByte(guidBytes, 3);
            packet.Translator.ReadXORByte(guidBytes, 6);
            packet.Translator.ReadXORByte(guidBytes, 7);
            packet.Translator.ReadXORByte(guidBytes, 2);

            packet.Translator.WriteGuid("Guid", guidBytes);
        }

        [Parser(Opcode.MSG_MOVE_SET_FACING, ClientVersionBuild.V4_2_2_14545, ClientVersionBuild.V4_3_0_15005)]
        public static void HandleMovementSetFacing422(Packet packet)
        {
            var info = new MovementInfo();
            var guidBytes = new byte[8];
            var transportGuidBytes = new byte[8];

            info.Flags = packet.Translator.ReadBitsE<MovementFlag>("Movement Flags", 30);

            guidBytes[4] = packet.Translator.ReadBit();
            guidBytes[2] = packet.Translator.ReadBit();

            info.HasSplineData = packet.Translator.ReadBit("HasSplineData");

            guidBytes[3] = packet.Translator.ReadBit();
            guidBytes[5] = packet.Translator.ReadBit();

            packet.Translator.ReadBitsE<MovementFlagExtra>("Extra Movement Flags", 12);

            guidBytes[0] = packet.Translator.ReadBit();
            guidBytes[7] = packet.Translator.ReadBit();
            guidBytes[6] = packet.Translator.ReadBit();
            guidBytes[1] = packet.Translator.ReadBit();

            var splineElevation = packet.Translator.ReadBit("HaveSplineElevation");

            var havePitch = packet.Translator.ReadBit("HavePitch");
            var haveFallData = packet.Translator.ReadBit("HaveFallData");
            var haveFallDirection = false;

            if (haveFallData)
                haveFallDirection = packet.Translator.ReadBit("HaveFallDirection");

            var haveTransportData = packet.Translator.ReadBit("HaveTransportData");

            var haveTransportTime2 = false;
            var haveTransportTime3 = false;

            if (haveTransportData)
            {
                transportGuidBytes = packet.Translator.StartBitStream(0, 6, 2, 5, 4, 1, 3, 7);
                haveTransportTime2 = packet.Translator.ReadBit("HaveTransportTime2");
                haveTransportTime3 = packet.Translator.ReadBit("HaveTransportTime3");
            }

            info.Orientation = packet.Translator.ReadSingle("Orientation");

            packet.Translator.ReadUInt32("Timestamp");

            info.Position = packet.Translator.ReadVector3("Position");

            packet.Translator.ParseBitStream(guidBytes, 7, 5);

            if (splineElevation)
                packet.Translator.ReadSingle("Spline Elevation");

            packet.Translator.ParseBitStream(guidBytes, 4, 1, 2);

            if (havePitch)
                packet.Translator.ReadSingle("Pitch");

            if (haveFallData)
            {
                packet.Translator.ReadUInt32("Fall Time");
                packet.Translator.ReadSingle("Fall Vertical Speed");
                packet.Translator.ReadSingle("Fall Horizontal Speed");

                if (haveFallDirection)
                {
                    packet.Translator.ReadSingle("Fall Cos Angle");
                    packet.Translator.ReadSingle("Fall Sin Angle");
                }
            }

            packet.Translator.ParseBitStream(guidBytes, 6, 0);

            if (haveTransportData)
            {
                packet.Translator.ReadByte("Transport Seat");
                packet.Translator.ReadSingle("Transport Orientation");
                packet.Translator.ReadVector3("Transport Position");

                packet.Translator.ReadUInt32("Transport Time");

                if (haveTransportTime2)
                    packet.Translator.ReadUInt32("Transport Time 2");

                packet.Translator.ParseBitStream(transportGuidBytes, 3, 6);

                if (haveTransportTime3)
                    packet.Translator.ReadUInt32("Transport Time 3");

                packet.Translator.ParseBitStream(transportGuidBytes, 7, 5, 2, 1, 0, 4);
            }

            packet.Translator.ParseBitStream(guidBytes, 3);

            packet.Translator.WriteGuid("Guid", guidBytes);
            packet.Translator.WriteGuid("Transport Guid", transportGuidBytes);
        }

        [Parser(Opcode.MSG_MOVE_TELEPORT, ClientVersionBuild.V4_2_2_14545, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleMoveTeleport422(Packet packet)
        {
            var onTransport = packet.Translator.ReadBit("OnTransport");

            var guid = packet.Translator.StartBitStream(0, 2, 6, 7, 4, 5, 3, 1);

            var unk2 = packet.Translator.ReadBit("Unk Bit Boolean 2");

            packet.Translator.ReadVector3("Destination Position");

            packet.Translator.ReadXORByte(guid, 5);
            packet.Translator.ReadXORByte(guid, 4);

            if (onTransport)
                packet.Translator.ReadGuid("Transport Guid");

            packet.Translator.ReadXORByte(guid, 2);
            packet.Translator.ReadXORByte(guid, 7);

            packet.Translator.ReadInt32("Unk 1");

            packet.Translator.ReadXORByte(guid, 1);
            packet.Translator.ReadXORByte(guid, 2);
            packet.Translator.ReadXORByte(guid, 6);
            packet.Translator.ReadXORByte(guid, 3);

            if (unk2)
                packet.Translator.ReadByte("Unk 2");

            packet.Translator.ReadSingle("Arrive Orientation");
            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.MSG_MOVE_TELEPORT, ClientVersionBuild.V5_1_0_16309)]
        public static void HandleMoveTeleport510(Packet packet)
        {
            var guid = new byte[8];
            var transGuid = new byte[8];
            var pos = new Vector4();

            packet.Translator.ReadUInt32("Unk");

            pos.X = packet.Translator.ReadSingle();
            pos.Y = packet.Translator.ReadSingle();
            pos.Z = packet.Translator.ReadSingle();
            pos.O = packet.Translator.ReadSingle();
            packet.AddValue("Destination", pos);

            guid[3] = packet.Translator.ReadBit();
            guid[1] = packet.Translator.ReadBit();
            guid[7] = packet.Translator.ReadBit();

            var bit48 = packet.Translator.ReadBit();

            guid[6] = packet.Translator.ReadBit();

            if (bit48)
            {
                packet.Translator.ReadBit("Unk bit 50");
                packet.Translator.ReadBit("Unk bit 51");
            }

            guid[0] = packet.Translator.ReadBit();
            guid[4] = packet.Translator.ReadBit();

            var onTransport = packet.Translator.ReadBit("On transport");
            guid[2] = packet.Translator.ReadBit();
            if (onTransport)
                transGuid = packet.Translator.StartBitStream(7, 5, 2, 1, 0, 4, 3, 6);

            guid[5] = packet.Translator.ReadBit();

            if (onTransport)
            {
                packet.Translator.ParseBitStream(transGuid, 1, 5, 7, 0, 3, 4, 6, 2);
                packet.Translator.WriteGuid("Transport Guid", transGuid);
            }

            packet.Translator.ReadXORByte(guid, 3);

            if (bit48)
                packet.Translator.ReadUInt32("Unk int");

            packet.Translator.ReadXORByte(guid, 2);
            packet.Translator.ReadXORByte(guid, 1);
            packet.Translator.ReadXORByte(guid, 7);
            packet.Translator.ReadXORByte(guid, 5);
            packet.Translator.ReadXORByte(guid, 6);
            packet.Translator.ReadXORByte(guid, 4);
            packet.Translator.ReadXORByte(guid, 0);

            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.MSG_MOVE_STOP, ClientVersionBuild.V4_2_2_14545, ClientVersionBuild.V4_3_0_15005)]
        public static void HandleMoveStop422(Packet packet)
        {
            var info = new MovementInfo();
            var guidBytes = new byte[8];
            var transportGuidBytes = new byte[8];

            guidBytes[2] = packet.Translator.ReadBit();
            guidBytes[0] = packet.Translator.ReadBit();

            info.HasSplineData = packet.Translator.ReadBit("HasSplineData");

            info.Flags = packet.Translator.ReadBitsE<MovementFlag>("Movement Flags", 30);

            guidBytes[4] = packet.Translator.ReadBit();
            guidBytes[6] = packet.Translator.ReadBit();
            guidBytes[3] = packet.Translator.ReadBit();
            guidBytes[5] = packet.Translator.ReadBit();
            guidBytes[7] = packet.Translator.ReadBit();

            packet.Translator.ReadBitsE<MovementFlagExtra>("Extra Movement Flags", 12);

            guidBytes[1] = packet.Translator.ReadBit();

            var havePitch = packet.Translator.ReadBit("HavePitch");

            var haveFallData = packet.Translator.ReadBit("HaveFallData");

            var haveFallDirection = false;
            if (haveFallData)
                haveFallDirection = packet.Translator.ReadBit("HaveFallDirection");

            var haveTransportData = packet.Translator.ReadBit("HaveTransportData");

            var haveTransportTime2 = false;
            var haveTransportTime3 = false;

            if (haveTransportData)
            {
                transportGuidBytes = packet.Translator.StartBitStream(0, 6, 2, 5, 4, 1, 3, 7);
                haveTransportTime2 = packet.Translator.ReadBit("HaveTransportTime2");
                haveTransportTime3 = packet.Translator.ReadBit("HaveTransportTime3");
            }

            var splineElevation = packet.Translator.ReadBit("HaveSplineElevation");

            info.Orientation = packet.Translator.ReadSingle("Orientation");

            packet.Translator.ReadUInt32("Timestamp");

            info.Position = packet.Translator.ReadVector3("Position");

            packet.Translator.ReadXORByte(guidBytes, 2);
            packet.Translator.ReadXORByte(guidBytes, 3);

            if (havePitch)
                packet.Translator.ReadSingle("Pitch");

            if (haveFallData)
            {
                packet.Translator.ReadUInt32("Fall Time");
                packet.Translator.ReadSingle("Fall Vertical Speed");
                packet.Translator.ReadSingle("Fall Horizontal Speed");

                if (haveFallDirection)
                {
                    packet.Translator.ReadSingle("Fall Cos Angle");
                    packet.Translator.ReadSingle("Fall Sin Angle");
                }
            }

            packet.Translator.ReadXORByte(guidBytes, 5);
            packet.Translator.ReadXORByte(guidBytes, 7);

            if (haveTransportData)
            {
                packet.Translator.ReadByte("Transport Seat");
                packet.Translator.ReadSingle("Transport Orientation");
                packet.Translator.ReadVector3("Transport Position");

                packet.Translator.ReadUInt32("Transport Time");

                if (haveTransportTime2)
                    packet.Translator.ReadUInt32("Transport Time 2");

                packet.Translator.ParseBitStream(transportGuidBytes, 3, 6);

                if (haveTransportTime3)
                    packet.Translator.ReadUInt32("Transport Time 3");

                packet.Translator.ParseBitStream(transportGuidBytes, 7, 5, 2, 1, 0, 4);
            }

            packet.Translator.ReadXORByte(guidBytes, 1);
            packet.Translator.ReadXORByte(guidBytes, 0);

            if (splineElevation)
                packet.Translator.ReadSingle("Spline Elevation");

            packet.Translator.ReadXORByte(guidBytes, 6);
            packet.Translator.ReadXORByte(guidBytes, 4);

            packet.Translator.WriteGuid("Guid", guidBytes);
            packet.Translator.WriteGuid("Transport Guid", transportGuidBytes);
        }

        [Parser(Opcode.SMSG_MOVE_UPDATE, ClientVersionBuild.V4_2_2_14545, ClientVersionBuild.V4_3_0_15005)]
        public static void HandlePlayerMove422(Packet packet)
        {
            var info = new MovementInfo();
            var guidBytes = new byte[8];
            var transportGuidBytes = new byte[8];

            var splineElevation = packet.Translator.ReadBit("HaveSplineElevation");
            var haveTransportData = packet.Translator.ReadBit("HaveTransportData");
            guidBytes[5] = packet.Translator.ReadBit();

            var haveTransportTime2 = false;
            var haveTransportTime3 = false;
            if (haveTransportData)
            {
                transportGuidBytes[2] = packet.Translator.ReadBit();
                transportGuidBytes[4] = packet.Translator.ReadBit();
                transportGuidBytes[1] = packet.Translator.ReadBit();
                transportGuidBytes[3] = packet.Translator.ReadBit();
                transportGuidBytes[0] = packet.Translator.ReadBit();
                haveTransportTime2 = packet.Translator.ReadBit("HaveTransportTime2");
                transportGuidBytes[7] = packet.Translator.ReadBit();
                haveTransportTime3 = packet.Translator.ReadBit("HaveTransportTime3");
                transportGuidBytes[6] = packet.Translator.ReadBit();
                transportGuidBytes[5] = packet.Translator.ReadBit();
            }

            guidBytes[7] = packet.Translator.ReadBit();
            guidBytes[3] = packet.Translator.ReadBit();
            guidBytes[1] = packet.Translator.ReadBit();
            guidBytes[4] = packet.Translator.ReadBit();
            guidBytes[0] = packet.Translator.ReadBit();
            info.Flags = packet.Translator.ReadBitsE<MovementFlag>("Movement Flags", 30);
            var havePitch = packet.Translator.ReadBit("HavePitch");
            guidBytes[2] = packet.Translator.ReadBit();
            packet.Translator.ReadBitsE<MovementFlagExtra>("Extra Movement Flags", 12);
            guidBytes[6] = packet.Translator.ReadBit();

            var haveFallData = packet.Translator.ReadBit("HaveFallData");
            var haveFallDirection = false;
            if (haveFallData)
                haveFallDirection = packet.Translator.ReadBit("HaveFallDirection");

            info.HasSplineData = packet.Translator.ReadBit("HasSplineData");

            packet.Translator.ReadXORByte(guidBytes, 4);
            packet.Translator.ReadXORByte(guidBytes, 0);

            info.Orientation = packet.Translator.ReadSingle("Orientation");

            packet.Translator.ReadXORByte(guidBytes, 6);
            packet.Translator.ReadXORByte(guidBytes, 7);

            if (splineElevation)
                packet.Translator.ReadSingle("Spline Elevation");

            if (haveTransportData)
            {
                packet.Translator.ReadXORByte(transportGuidBytes, 4);
                packet.Translator.ReadXORByte(transportGuidBytes, 2);
                packet.Translator.ReadSingle("Transport Orientation");
                packet.Translator.ReadUInt32("Transport Time");
                packet.Translator.ReadByte("Transport Seat");
                packet.Translator.ReadXORByte(transportGuidBytes, 3);
                packet.Translator.ReadVector3("Transport Position");
                packet.Translator.ReadXORByte(transportGuidBytes, 1);

                if (haveTransportTime2)
                    packet.Translator.ReadUInt32("Transport Time 2");

                if (haveTransportTime3)
                    packet.Translator.ReadUInt32("Transport Time 3");

                packet.Translator.ReadXORByte(transportGuidBytes, 5);
                packet.Translator.ReadXORByte(transportGuidBytes, 0);
                packet.Translator.ReadXORByte(transportGuidBytes, 6);
                packet.Translator.ReadXORByte(transportGuidBytes, 7);
            }

            packet.Translator.ReadXORByte(guidBytes, 2);
            packet.Translator.ReadUInt32("Timestamp");
            packet.Translator.ReadXORByte(guidBytes, 1);

            if (havePitch)
                packet.Translator.ReadSingle("Pitch");

            info.Position = packet.Translator.ReadVector3("Position");
            packet.Translator.ReadXORByte(guidBytes, 5);
            packet.Translator.ReadXORByte(guidBytes, 3);

            if (haveFallData)
            {
                packet.Translator.ReadSingle("Fall Horizontal Speed");

                if (haveFallDirection)
                {
                    packet.Translator.ReadSingle("Fall Cos Angle");
                    packet.Translator.ReadSingle("Fall Sin Angle");
                }

                packet.Translator.ReadSingle("Fall Vertical Speed");
                packet.Translator.ReadUInt32("Fall Time");
            }

            packet.Translator.WriteGuid("Guid", guidBytes);
            packet.Translator.WriteGuid("Transport Guid", transportGuidBytes);
        }

        [Parser(Opcode.SMSG_MOVE_UPDATE, ClientVersionBuild.Zero, ClientVersionBuild.V4_2_2_14545)]
        public static void HandlePlayerMove(Packet packet)
        {
        }

        [Parser(Opcode.MSG_MOVE_START_FORWARD, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_START_BACKWARD, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_STOP, ClientVersionBuild.Zero, ClientVersionBuild.V4_2_2_14545)]
        [Parser(Opcode.MSG_MOVE_START_STRAFE_LEFT, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_START_STRAFE_RIGHT, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_STOP_STRAFE, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_START_ASCEND, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_START_DESCEND, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_STOP_ASCEND, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_JUMP, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_START_TURN_LEFT, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_START_TURN_RIGHT, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_STOP_TURN, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_START_PITCH_UP, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_START_PITCH_DOWN, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_STOP_PITCH, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_SET_RUN_MODE, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_SET_WALK_MODE, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_TELEPORT, ClientVersionBuild.Zero, ClientVersionBuild.V4_2_2_14545)]
        [Parser(Opcode.MSG_MOVE_SET_FACING, ClientVersionBuild.Zero, ClientVersionBuild.V4_2_2_14545)]
        [Parser(Opcode.MSG_MOVE_SET_PITCH, ClientVersionBuild.Zero, ClientVersionBuild.V4_2_2_14545)]
        [Parser(Opcode.MSG_MOVE_TOGGLE_COLLISION_CHEAT)]
        [Parser(Opcode.MSG_MOVE_GRAVITY_CHNG)]
        [Parser(Opcode.MSG_MOVE_ROOT)]
        [Parser(Opcode.MSG_MOVE_UNROOT)]
        [Parser(Opcode.MSG_MOVE_START_SWIM, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_STOP_SWIM, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_START_SWIM_CHEAT)]
        [Parser(Opcode.MSG_MOVE_STOP_SWIM_CHEAT)]
        [Parser(Opcode.MSG_MOVE_HEARTBEAT, ClientVersionBuild.Zero, ClientVersionBuild.V4_2_2_14545)]
        [Parser(Opcode.MSG_MOVE_FALL_LAND, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.MSG_MOVE_UPDATE_CAN_FLY)]
        [Parser(Opcode.MSG_MOVE_UPDATE_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY)]
        [Parser(Opcode.MSG_MOVE_KNOCK_BACK)]
        [Parser(Opcode.MSG_MOVE_HOVER)]
        [Parser(Opcode.MSG_MOVE_FEATHER_FALL)]
        [Parser(Opcode.MSG_MOVE_WATER_WALK)]
        [Parser(Opcode.CMSG_MOVE_FALL_RESET, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.CMSG_MOVE_SET_FLY)]
        [Parser(Opcode.CMSG_MOVE_CHNG_TRANSPORT, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.CMSG_MOVE_NOT_ACTIVE_MOVER, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.CMSG_DISMISS_CONTROLLED_VEHICLE, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleMovementMessages(Packet packet)
        {
            WowGuid guid;
            if ((ClientVersion.AddedInVersion(ClientVersionBuild.V3_2_0_10192) ||
                packet.Direction == Direction.ServerToClient) && ClientVersion.Build != ClientVersionBuild.V4_2_2_14545)
                guid = packet.Translator.ReadPackedGuid("Guid");
            else
                guid = new WowGuid64();

            ReadMovementInfo(packet, guid);

            if (packet.Opcode != Opcodes.GetOpcode(Opcode.MSG_MOVE_KNOCK_BACK, Direction.Bidirectional))
                return;

            packet.Translator.ReadSingle("Sin Angle");
            packet.Translator.ReadSingle("Cos Angle");
            packet.Translator.ReadSingle("Speed");
            packet.Translator.ReadSingle("Velocity");
        }

        [Parser(Opcode.CMSG_MOVE_SPLINE_DONE, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleMoveSplineDone(Packet packet)
        {
            var guid = packet.Translator.ReadPackedGuid("Guid");
            ReadMovementInfo(packet, guid);
            packet.Translator.ReadInt32("Movement Counter"); // Possibly
        }

        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_RUN_SPEED, ClientVersionBuild.V4_2_2_14545, ClientVersionBuild.V4_3_0_15005)]
        public static void HandleSplineMovementSetRunSpeed422(Packet packet)
        {
            var guid = packet.Translator.StartBitStream(7, 2, 1, 3, 5, 6, 4, 0);
            packet.Translator.ParseBitStream(guid, 6, 7, 4, 3, 2, 5, 0, 1);
            packet.Translator.ReadSingle("Speed");
            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_RUN_SPEED, ClientVersionBuild.V4_3_0_15005, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleSplineMovementSetRunSpeed430(Packet packet)
        {
            var guid = packet.Translator.StartBitStream(2, 6, 4, 1, 3, 0, 7, 5);
            packet.Translator.ParseBitStream(guid, 2, 4, 3);
            packet.Translator.ReadSingle("Speed");
            packet.Translator.ParseBitStream(guid, 0, 6, 5, 1, 7);
            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.SMSG_MOVE_SET_RUN_SPEED, ClientVersionBuild.V5_1_0_16309)]
        public static void HandleMoveSetRunSpeed510(Packet packet)
        {
            var guid = packet.Translator.StartBitStream(0, 4, 1, 6, 3, 5, 7, 2);
            packet.Translator.ReadSingle("Speed");
            packet.Translator.ReadXORByte(guid, 7);
            packet.Translator.ReadInt32("Unk Int32");
            packet.Translator.ParseBitStream(guid, 3, 6, 0, 4, 1, 5, 2);
            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.MSG_MOVE_SET_RUN_SPEED, ClientVersionBuild.V4_2_2_14545)]
        public static void HandleMovementSetRunSpeed422(Packet packet)
        {
            var guid = packet.Translator.StartBitStream(1, 0, 7, 5, 2, 4, 3, 6);
            packet.Translator.ParseBitStream(guid, 1);
            packet.Translator.ReadSingle("Speed");
            packet.Translator.ParseBitStream(guid, 6, 2, 3, 7, 4, 0, 5);
            packet.Translator.ReadUInt32("Move Event");
            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.MSG_MOVE_SET_WALK_SPEED)]
        [Parser(Opcode.MSG_MOVE_SET_RUN_SPEED, ClientVersionBuild.Zero, ClientVersionBuild.V4_2_2_14545)]
        [Parser(Opcode.MSG_MOVE_SET_RUN_BACK_SPEED)]
        [Parser(Opcode.MSG_MOVE_SET_SWIM_SPEED)]
        [Parser(Opcode.MSG_MOVE_SET_SWIM_BACK_SPEED)]
        [Parser(Opcode.MSG_MOVE_SET_TURN_RATE)]
        [Parser(Opcode.MSG_MOVE_SET_FLIGHT_SPEED)]
        [Parser(Opcode.MSG_MOVE_SET_FLIGHT_BACK_SPEED)]
        [Parser(Opcode.MSG_MOVE_SET_PITCH_RATE)]
        public static void HandleMovementSetSpeed(Packet packet)
        {
            var guid = packet.Translator.ReadPackedGuid("GUID");
            ReadMovementInfo(packet, guid);
            packet.Translator.ReadSingle("Speed");
        }

        [Parser(Opcode.SMSG_FORCE_WALK_SPEED_CHANGE)]
        [Parser(Opcode.SMSG_FORCE_RUN_SPEED_CHANGE)]
        [Parser(Opcode.SMSG_FORCE_RUN_BACK_SPEED_CHANGE)]
        [Parser(Opcode.SMSG_FORCE_SWIM_SPEED_CHANGE)]
        [Parser(Opcode.SMSG_FORCE_SWIM_BACK_SPEED_CHANGE)]
        [Parser(Opcode.SMSG_FORCE_TURN_RATE_CHANGE)]
        [Parser(Opcode.SMSG_FORCE_FLIGHT_SPEED_CHANGE)]
        [Parser(Opcode.SMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE)]
        [Parser(Opcode.SMSG_FORCE_PITCH_RATE_CHANGE)]
        public static void HandleForceSpeedChange(Packet packet)
        {
            packet.Translator.ReadPackedGuid("Guid");
            packet.Translator.ReadUInt32("Movement Counter");

            if (packet.Opcode == Opcodes.GetOpcode(Opcode.SMSG_FORCE_RUN_SPEED_CHANGE, Direction.ServerToClient))
                packet.Translator.ReadByte("Unk Byte");

            packet.Translator.ReadSingle("New Speed");
        }

        [Parser(Opcode.CMSG_FORCE_RUN_SPEED_CHANGE_ACK)]
        [Parser(Opcode.CMSG_FORCE_RUN_BACK_SPEED_CHANGE_ACK)]
        [Parser(Opcode.CMSG_FORCE_SWIM_SPEED_CHANGE_ACK)]
        [Parser(Opcode.CMSG_FORCE_SWIM_BACK_SPEED_CHANGE_ACK)]
        [Parser(Opcode.CMSG_FORCE_WALK_SPEED_CHANGE_ACK)]
        [Parser(Opcode.CMSG_FORCE_TURN_RATE_CHANGE_ACK)]
        [Parser(Opcode.CMSG_FORCE_FLIGHT_SPEED_CHANGE_ACK)]
        [Parser(Opcode.CMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE_ACK)]
        public static void HandleSpeedChangeMessage(Packet packet)
        {
            var guid = packet.Translator.ReadPackedGuid("Guid");
            packet.Translator.ReadInt32("Movement Counter");

            ReadMovementInfo(packet, guid);

            packet.Translator.ReadSingle("New Speed");
        }

        [Parser(Opcode.MSG_MOVE_SET_COLLISION_HGT)]
        [Parser(Opcode.SMSG_MOVE_SET_COLLISION_HGT)]
        [Parser(Opcode.CMSG_MOVE_SET_COLLISION_HGT_ACK)]
        public static void HandleCollisionMovements(Packet packet)
        {
            var guid = packet.Translator.ReadPackedGuid("Guid");

            if (packet.Opcode != Opcodes.GetOpcode(Opcode.MSG_MOVE_SET_COLLISION_HGT, Direction.Bidirectional))
                packet.Translator.ReadInt32("Movement Counter");

            if (packet.Opcode != Opcodes.GetOpcode(Opcode.SMSG_MOVE_SET_COLLISION_HGT, Direction.ServerToClient))
                ReadMovementInfo(packet, guid);

            packet.Translator.ReadSingle("Collision Height");
        }

        [Parser(Opcode.CMSG_SET_ACTIVE_MOVER, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_0_15005)]
        [Parser(Opcode.SMSG_MOUNT_SPECIAL_ANIM)]
        public static void HandleSetActiveMover(Packet packet)
        {
            packet.Translator.ReadGuid("GUID");
        }

        [Parser(Opcode.CMSG_SET_ACTIVE_MOVER, ClientVersionBuild.V4_3_0_15005, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleSetActiveMover430(Packet packet)
        {
            var guid = packet.Translator.StartBitStream(7, 2, 0, 4, 3, 5, 6, 1);
            packet.Translator.ParseBitStream(guid, 1, 3, 2, 6, 0, 5, 4, 7);
            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.SMSG_FORCE_MOVE_ROOT)]
        [Parser(Opcode.SMSG_FORCE_MOVE_UNROOT)]
        [Parser(Opcode.SMSG_MOVE_SET_WATER_WALK)]
        [Parser(Opcode.SMSG_MOVE_SET_LAND_WALK)]
        [Parser(Opcode.SMSG_MOVE_SET_HOVERING)]
        [Parser(Opcode.SMSG_MOVE_UNSET_HOVERING)]
        [Parser(Opcode.SMSG_MOVE_SET_CAN_FLY)]
        [Parser(Opcode.SMSG_MOVE_UNSET_CAN_FLY)]
        [Parser(Opcode.SMSG_MOVE_ENABLE_TRANSITION_BETWEEN_SWIM_AND_FLY)]
        [Parser(Opcode.SMSG_MOVE_DISABLE_TRANSITION_BETWEEN_SWIM_AND_FLY)]
        [Parser(Opcode.SMSG_MOVE_SET_FEATHER_FALL)]
        [Parser(Opcode.SMSG_MOVE_SET_NORMAL_FALL, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleSetMovementMessages(Packet packet)
        {
            packet.Translator.ReadPackedGuid("Guid");
            packet.Translator.ReadInt32("Movement Counter");
        }

        [Parser(Opcode.CMSG_MOVE_WATER_WALK_ACK, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.CMSG_MOVE_FEATHER_FALL_ACK, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.CMSG_MOVE_HOVER_ACK, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.CMSG_MOVE_SET_CAN_FLY_ACK, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.CMSG_MOVE_SET_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY_ACK, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleSpecialMoveAckMessages(Packet packet)
        {
            var guid = packet.Translator.ReadPackedGuid("Guid");
            packet.Translator.ReadInt32("Movement Counter");
            ReadMovementInfo(packet, guid);
            packet.Translator.ReadSingle("Unk float");
        }

        [Parser(Opcode.CMSG_MOVE_KNOCK_BACK_ACK, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.CMSG_FORCE_MOVE_UNROOT_ACK, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.CMSG_FORCE_MOVE_ROOT_ACK, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleSpecialMoveAckMessages2(Packet packet)
        {
            WowGuid guid;
            if (ClientVersion.Build < ClientVersionBuild.V3_0_2_9056)
                guid = packet.Translator.ReadGuid("Guid");
            else
                guid = packet.Translator.ReadPackedGuid("Guid");

            packet.Translator.ReadInt32("Movement Counter");

            ReadMovementInfo(packet, guid);
        }

        [HasSniffData]
        [Parser(Opcode.SMSG_PHASE_SHIFT_CHANGE, ClientVersionBuild.Zero, ClientVersionBuild.V4_0_6a_13623)]
        public static void HandlePhaseShift(Packet packet)
        {
            CurrentPhaseMask = packet.Translator.ReadInt32("Phase Mask");

            packet.AddSniffData(StoreNameType.Phase, CurrentPhaseMask, "PHASEMASK");
        }

        [HasSniffData]
        [Parser(Opcode.SMSG_PHASE_SHIFT_CHANGE, ClientVersionBuild.V4_0_6a_13623, ClientVersionBuild.V4_1_0_13914)]
        public static void HandlePhaseShift406(Packet packet)
        {
            packet.Translator.ReadGuid("GUID");
            var i = 0;
            int count = packet.Translator.ReadInt32("Count");
            for (var j = 0; j < count / 2; ++j)
                packet.Translator.ReadInt16<MapId>("Unk", i, j);

            i++;
            count = packet.Translator.ReadInt32();
            for (var j = 0; j < count / 2; ++j)
                packet.Translator.ReadInt16<MapId>("Terrain Swap 1", i, j);

            i++;
            count = packet.Translator.ReadInt32();
            var phaseMask = 0;
            for (var j = 0; j < count / 2; ++j)
                phaseMask = packet.Translator.ReadInt16("Phases", ++i, j);

            i++;
            count = packet.Translator.ReadInt32();
            for (var j = 0; j < count / 2; ++j)
                packet.Translator.ReadInt16<MapId>("Terrain Swap 2", i, j);

            packet.Translator.ReadUInt32("Flag"); // can be 0, 4 or 8, 8 = normal world, others are unknown

            //CurrentPhaseMask = phaseMask;
            packet.AddSniffData(StoreNameType.Phase, phaseMask, "PHASEMASK 406");
        }

        [HasSniffData]
        [Parser(Opcode.SMSG_PHASE_SHIFT_CHANGE, ClientVersionBuild.V4_2_2_14545, ClientVersionBuild.V4_3_0_15005)]
        public static void HandlePhaseShift422(Packet packet)
        {
            var guid = new byte[8];

            guid[6] = packet.Translator.ReadBit();//0
            guid[1] = packet.Translator.ReadBit();//1
            guid[7] = packet.Translator.ReadBit();//2
            guid[4] = packet.Translator.ReadBit();//3
            guid[2] = packet.Translator.ReadBit();//4
            guid[3] = packet.Translator.ReadBit();//5
            guid[0] = packet.Translator.ReadBit();//6
            guid[5] = packet.Translator.ReadBit();//7

            packet.Translator.ReadXORByte(guid, 0);
            packet.Translator.ReadXORByte(guid, 4);

            var i = 0;
            var count = packet.Translator.ReadInt32();
            for (var j = 0; j < count / 2; ++j)
                packet.Translator.ReadInt16<MapId>("Map Swap 1", i, j);

            packet.Translator.ReadXORByte(guid, 3);

            packet.Translator.ReadUInt32("Mask");

            packet.Translator.ReadXORByte(guid, 2);

            var phaseMask = -1;
            count = packet.Translator.ReadInt32();
            for (var j = 0; j < count / 2; ++j)
                phaseMask = packet.Translator.ReadUInt16("Current Mask", i, j);

            packet.Translator.ReadXORByte(guid, 6);

            i++;
            count = packet.Translator.ReadInt32();
            for (var j = 0; j < count / 2; ++j)
                packet.Translator.ReadInt16<MapId>("Map Swap 1", i, j);

            packet.Translator.ReadXORByte(guid, 7);

            i++;
            count = packet.Translator.ReadInt32();
            for (var j = 0; j < count / 2; ++j)
                packet.Translator.ReadInt16<MapId>("Map Swap 3", i, j);

            packet.Translator.ReadXORByte(guid, 5);
            packet.Translator.ReadXORByte(guid, 1);

            packet.Translator.WriteGuid("Guid", guid);

            if (phaseMask != -1)
            {
                CurrentPhaseMask = phaseMask;
                packet.AddSniffData(StoreNameType.Phase, phaseMask, "PHASEMASK 422");
            }
        }

        [Parser(Opcode.SMSG_PHASE_SHIFT_CHANGE, ClientVersionBuild.V5_0_5_16048, ClientVersionBuild.V5_1_0_16309)]
        public static void HandlePhaseShift505(Packet packet)
        {
            ActivePhases.Clear();

            var count = packet.Translator.ReadUInt32() / 2;
            packet.AddValue("Inactive Terrain swap count", count);
            for (var i = 0; i < count; ++i)
                packet.Translator.ReadInt16<MapId>("Inactive Terrain swap", i);

            packet.Translator.ReadUInt32("UInt32");

            count = packet.Translator.ReadUInt32() / 2;
            packet.AddValue("Active Terrain swap count", count);
            for (var i = 0; i < count; ++i)
                packet.Translator.ReadInt16<MapId>("Active Terrain swap", i);

            count = packet.Translator.ReadUInt32() / 2;
            packet.AddValue("Phases count", count);
            for (var i = 0; i < count; ++i)
                ActivePhases.Add(packet.Translator.ReadUInt16("Phase id", i)); // Phase.dbc

            count = packet.Translator.ReadUInt32() / 2;
            packet.AddValue("WorldMapArea swap count", count);
            for (var i = 0; i < count; ++i)
                packet.Translator.ReadUInt16("WorldMapArea swap", i);

            var guid = packet.Translator.StartBitStream(3, 7, 1, 6, 0, 4, 5, 2);
            packet.Translator.ParseBitStream(guid, 4, 3, 0, 6, 2, 7, 5, 1);

            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.SMSG_PHASE_SHIFT_CHANGE, ClientVersionBuild.V5_1_0_16309)]
        public static void HandlePhaseShift510(Packet packet)
        {
            ActivePhases.Clear();

            var guid = packet.Translator.StartBitStream(6, 4, 7, 2, 0, 1, 3, 5);
            packet.Translator.ReadXORByte(guid, 4);

            var count = packet.Translator.ReadUInt32() / 2;
            packet.AddValue("WorldMapArea swap count", count);
            for (var i = 0; i < count; ++i)
                packet.Translator.ReadUInt16("WorldMapArea swap", i);

            packet.Translator.ReadXORByte(guid, 2);
            packet.Translator.ReadXORByte(guid, 3);

            count = packet.Translator.ReadUInt32() / 2;
            packet.AddValue("Phases count", count);
            for (var i = 0; i < count; ++i)
                ActivePhases.Add(packet.Translator.ReadUInt16("Phase id", i)); // Phase.dbc

            packet.Translator.ReadXORByte(guid, 1);
            packet.Translator.ReadXORByte(guid, 6);

            count = packet.Translator.ReadUInt32() / 2;
            packet.AddValue("Active Terrain swap count", count);
            for (var i = 0; i < count; ++i)
                packet.Translator.ReadInt16<MapId>("Active Terrain swap", i);

            packet.Translator.ReadUInt32("UInt32");
            packet.Translator.ReadXORByte(guid, 0);
            packet.Translator.ReadXORByte(guid, 7);
            packet.Translator.ReadXORByte(guid, 5);

            count = packet.Translator.ReadUInt32() / 2;
            packet.AddValue("Inactive Terrain swap count", count);
            for (var i = 0; i < count; ++i)
                packet.Translator.ReadInt16<MapId>("Inactive Terrain swap", i);

            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.SMSG_TRANSFER_PENDING, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_0_15005)]
        public static void HandleTransferPending(Packet packet)
        {
            packet.Translator.ReadInt32<MapId>("Map ID");

            if (!packet.CanRead())
                return;

            packet.Translator.ReadInt32("Transport Entry");
            packet.Translator.ReadInt32<MapId>("Transport Map ID");
        }

        [Parser(Opcode.SMSG_TRANSFER_PENDING, ClientVersionBuild.V4_3_0_15005, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleTransferPending430(Packet packet)
        {
            var bit1 = packet.Translator.ReadBit();
            var hasTransport = packet.Translator.ReadBit();

            if (bit1)
                packet.Translator.ReadUInt32("Unk int");

            if (hasTransport)
            {
                packet.Translator.ReadInt32<MapId>("Transport Map ID");
                packet.Translator.ReadInt32("Transport Entry");
            }

            packet.Translator.ReadInt32<MapId>("Map ID");
        }

        [Parser(Opcode.SMSG_TRANSFER_ABORTED)]
        public static void HandleTransferAborted(Packet packet)
        {
            packet.Translator.ReadInt32<MapId>("Map ID");

            var reason = packet.Translator.ReadByteE<TransferAbortReason>("Reason");

            switch (reason)
            {
                case TransferAbortReason.DifficultyUnavailable:
                {
                    packet.Translator.ReadByteE<MapDifficulty>("Difficulty");
                    break;
                }
                case TransferAbortReason.InsufficientExpansion:
                {
                    packet.Translator.ReadByteE<ClientType>("Expansion");
                    break;
                }
                case TransferAbortReason.UniqueMessage:
                {
                    packet.Translator.ReadByte("Message ID");
                    break;
                }
                default:
                    packet.Translator.ReadByte(); // Does nothing
                    break;
            }
        }

        [Parser(Opcode.SMSG_FLIGHT_SPLINE_SYNC)]
        public static void HandleFlightSplineSync(Packet packet)
        {
            packet.Translator.ReadSingle("Duration modifier");
            packet.Translator.ReadPackedGuid("GUID");
        }

        [Parser(Opcode.SMSG_CONTROL_UPDATE)]
        public static void HandleClientControlUpdate(Packet packet)
        {
            packet.Translator.ReadPackedGuid("GUID");
            packet.Translator.ReadByte("AllowMove");
        }

        [Parser(Opcode.SMSG_MOVE_KNOCK_BACK, ClientVersionBuild.Zero, ClientVersionBuild.V4_2_2_14545)]
        public static void HandleMoveKnockBack(Packet packet)
        {
            packet.Translator.ReadPackedGuid("GUID");
            packet.Translator.ReadUInt32("Movement Counter");
            packet.Translator.ReadSingle("X direction");
            packet.Translator.ReadSingle("Y direction");
            packet.Translator.ReadSingle("Horizontal Speed");
            packet.Translator.ReadSingle("Vertical Speed");
        }

        [Parser(Opcode.MSG_MOVE_TIME_SKIPPED)]
        public static void HandleMoveTimeSkippedMsg(Packet packet)
        {
            packet.Translator.ReadPackedGuid("Guid");
            packet.Translator.ReadUInt32("Time");
        }

        [Parser(Opcode.CMSG_MOVE_TIME_SKIPPED, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleMoveTimeSkipped(Packet packet)
        {
            if (ClientVersion.Build < ClientVersionBuild.V3_0_2_9056)
                packet.Translator.ReadGuid("GUID");
            else
                packet.Translator.ReadPackedGuid("GUID");
            packet.Translator.ReadUInt32("Time");
        }

        [Parser(Opcode.SMSG_MOVE_SPLINE_ROOT, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_UNROOT, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_ENABLE_GRAVITY, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_DISABLE_GRAVITY, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_FEATHER_FALL, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_NORMAL_FALL, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_HOVER, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_UNSET_HOVER, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_WATER_WALK, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_LAND_WALK)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_START_SWIM, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_STOP_SWIM, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_RUN_MODE, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_WALK_MODE, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleSplineMovementMessages(Packet packet)
        {
            packet.Translator.ReadPackedGuid("GUID");
        }

        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_FLYING, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_UNSET_FLYING, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleSplineFlyMovementMessages(Packet packet)
        {
            WowGuid guid = packet.Translator.ReadPackedGuid("Guid");
            ReadMovementInfo(packet, guid);
        }

        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_WALK_BACK_SPEED, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_RUN_SPEED, ClientVersionBuild.Zero, ClientVersionBuild.V4_2_2_14545)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_SWIM_SPEED, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_FLIGHT_SPEED, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_RUN_BACK_SPEED, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_SWIM_BACK_SPEED, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_FLIGHT_BACK_SPEED, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_TURN_RATE, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        [Parser(Opcode.SMSG_MOVE_SPLINE_SET_PITCH_RATE, ClientVersionBuild.Zero, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleSplineMovementSetSpeed(Packet packet)
        {
            packet.Translator.ReadPackedGuid("GUID");
            packet.Translator.ReadSingle("Amount");
        }

        [Parser(Opcode.SMSG_COMPRESSED_MOVES)]
        public static void HandleCompressedMoves(Packet packet)
        {
            packet.Formatter.OpenCollection(""); // To be able to see what is inside this packet.
            //packet.WriteLine();

            using (var pkt = packet.Inflate(packet.Translator.ReadInt32()))
            {
                while (pkt.CanRead())
                {
                    var size = pkt.ReadByte();
                    var opc = pkt.ReadInt16();
                    var data = pkt.ReadBytes(size - 2);

                    using (var newPacket = new Packet(data, opc, pkt.Time, pkt.Direction, pkt.Number, packet.Formatter, packet.FileName))
                        Handler.Parse(newPacket, true);
                    //packet.WriteLine();
                }
            }

            packet.Formatter.CloseCollection("");
            packet.ReadToEnd();
        }

        [Parser(Opcode.SMSG_MOVE_KNOCK_BACK, ClientVersionBuild.V4_2_2_14545)]
        public static void HandleMoveKnockBack422(Packet packet)
        {
            var guid = packet.Translator.StartBitStream(5, 2, 6, 3, 1, 4, 0, 7);

            packet.Translator.ReadXORByte(guid, 0);

            packet.Translator.ReadSingle("Jump Velocity");
            packet.Translator.ReadUInt32("Fall time");
            packet.Translator.ReadSingle("Fall Start Velocity");

            packet.Translator.ReadXORByte(guid, 6);

            packet.Translator.ReadSingle("Jump Cos");
            packet.Translator.ReadSingle("Jump Sin");

            packet.Translator.ReadXORByte(guid, 3);
            packet.Translator.ReadXORByte(guid, 1);
            packet.Translator.ReadXORByte(guid, 2);
            packet.Translator.ReadXORByte(guid, 4);
            packet.Translator.ReadXORByte(guid, 7);
            packet.Translator.ReadXORByte(guid, 5);

            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.SMSG_SET_PLAY_HOVER_ANIM)]
        public static void HandlePlayHoverAnim(Packet packet)
        {
            var guid = new byte[8];
            guid[4] = packet.Translator.ReadBit();
            guid[0] = packet.Translator.ReadBit();
            guid[1] = packet.Translator.ReadBit();
            packet.Translator.ReadBit("unk");
            guid[3] = packet.Translator.ReadBit();
            guid[7] = packet.Translator.ReadBit();
            guid[5] = packet.Translator.ReadBit();
            guid[2] = packet.Translator.ReadBit();
            guid[6] = packet.Translator.ReadBit();

            packet.Translator.ParseBitStream(guid, 3, 2, 1, 7, 0, 5, 4, 6);
            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.SMSG_MOVE_SET_ACTIVE_MOVER, ClientVersionBuild.V4_3_0_15005, ClientVersionBuild.V4_3_4_15595)]
        public static void HandleMoveSetActiveMover430(Packet packet)
        {
            var guid = packet.Translator.StartBitStream(6, 2, 7, 0, 3, 5, 4, 1);
            packet.Translator.ParseBitStream(guid, 3, 5, 6, 7, 2, 0, 1, 4);
            packet.Translator.WriteGuid("Guid", guid);
        }

        [Parser(Opcode.CMSG_MOUNT_SPECIAL_ANIM)]
        public static void HandleMovementNull(Packet packet)
        {
        }
    }
}
