using System;
using System.Collections.Generic;
using WowPacketParser.Enums;
using WowPacketParser.Misc;
using WowPacketParser.Parsing;
using WowPacketParser.Store;
using WowPacketParser.Store.Objects;
using CoreParsers = WowPacketParser.Parsing.Parsers;

namespace WowPacketParserModule.V5_4_2_17658.Parsers
{
    public static class UpdateHandler
    {
        [HasSniffData] // in ReadCreateObjectBlock
        [Parser(Opcode.SMSG_UPDATE_OBJECT)]
        public static void HandleUpdateObject(Packet packet)
        {
            uint map = packet.Translator.ReadUInt16("Map");
            var count = packet.Translator.ReadUInt32("Count");

            for (var i = 0; i < count; i++)
            {
                var type = packet.Translator.ReadByte();
                var typeString = ((UpdateTypeCataclysm)type).ToString();

                packet.AddValue("UpdateType", typeString, i);
                switch (typeString)
                {
                    case "Values":
                        {
                            var guid = packet.Translator.ReadPackedGuid("GUID", i);

                            WoWObject obj;
                            var updates = CoreParsers.UpdateHandler.ReadValuesUpdateBlock(packet, guid.GetObjectType(), i, false);

                            if (Storage.Objects.TryGetValue(guid, out obj))
                            {
                                if (obj.ChangedUpdateFieldsList == null)
                                    obj.ChangedUpdateFieldsList = new List<Dictionary<int, UpdateField>>();
                                obj.ChangedUpdateFieldsList.Add(updates);
                            }

                            break;
                        }
                    case "CreateObject1":
                    case "CreateObject2": // Might != CreateObject1 on Cata
                        {
                            var guid = packet.Translator.ReadPackedGuid("GUID", i);
                            ReadCreateObjectBlock(packet, guid, map, i);
                            break;
                        }
                    case "DestroyObjects":
                        {
                            CoreParsers.UpdateHandler.ReadObjectsBlock(packet, i);
                            break;
                        }
                }
            }
        }

        private static void ReadCreateObjectBlock(Packet packet, WowGuid guid, uint map, object index)
        {
            var objType = packet.Translator.ReadByteE<ObjectType>("Object Type", index);
            var moves = ReadMovementUpdateBlock(packet, guid, index);
            var updates = CoreParsers.UpdateHandler.ReadValuesUpdateBlock(packet, objType, index, true);

            WoWObject obj;
            switch (objType)
            {
                case ObjectType.Unit:
                    obj = new Unit();
                    break;
                case ObjectType.GameObject:
                    obj = new GameObject();
                    break;
                case ObjectType.Item:
                    obj = new Item();
                    break;
                case ObjectType.Player:
                    obj = new Player();
                    break;
                default:
                    obj = new WoWObject();
                    break;
            }

            obj.Type = objType;
            obj.Movement = moves;
            obj.UpdateFields = updates;
            obj.Map = map;
            obj.Area = CoreParsers.WorldStateHandler.CurrentAreaId;
            obj.PhaseMask = (uint)CoreParsers.MovementHandler.CurrentPhaseMask;
            obj.Phases = new HashSet<ushort>(CoreParsers.MovementHandler.ActivePhases);

            // If this is the second time we see the same object (same guid,
            // same position) update its phasemask
            if (Storage.Objects.ContainsKey(guid))
            {
                var existObj = Storage.Objects[guid].Item1;
                CoreParsers.UpdateHandler.ProcessExistingObject(ref existObj, obj, guid); // can't do "ref Storage.Objects[guid].Item1 directly
            }
            else
                Storage.Objects.Add(guid, obj, packet.TimeSpan);

            if (guid.HasEntry() && (objType == ObjectType.Unit || objType == ObjectType.GameObject))
                packet.AddSniffData(Utilities.ObjectTypeToStore(objType), (int)guid.GetEntry(), "SPAWN");
        }

        private static MovementInfo ReadMovementUpdateBlock(Packet packet, WowGuid guid, object index)
        {
            var moveInfo = new MovementInfo();

            var guid1 = new byte[8];
            var transportGuid = new byte[8];
            var goTransportGuid = new byte[8];
            var attackingTargetGuid = new byte[8];

            var hasAnimKit1 = false;
            var hasAnimKit2 = false;
            var hasAnimKit3 = false;
            var hasFullSpline = false;
            var hasSplineStartTime = false;
            var hasSplineVerticalAcceleration = false;
            var hasGOTransportTime2 = false;
            var hasGOTransportTime3 = false;
            var hasSplineElevation = false;
            var hasMovementFlags = false;
            var hasMoveFlagsExtra = false;
            var hasTimestamp = false;
            var hasOrientation = false;
            var hasTransportData = false;
            var hasTransportTime3 = false;
            var hasTransportTime2 = false;
            var hasPitch = false;
            var hasFallData = false;
            var hasFallDirection = false;

            var bit130 = false;
            var bitA0 = false;
            var bit23C = false;
            var bit298 = false;
            var bit230 = false;
            var bit284 = false;
            var bit228 = false;
            var bit258 = false;
            var bit218 = false;
            var bit220 = false;

            var bits160 = 0u;
            var splineCount = 0u;
            var bits11C = 0u;
            var bits90 = 0u;
            var bits25C = 0u;
            var bits26C = 0u;
            var bits288 = 0u;
            var bits418 = 0u;
            var bits2AA = 0u;
            SplineType splineType = SplineType.Stop;

            var hasVehicleData = packet.Translator.ReadBit("Has Vehicle Data", index);
            var bit2A8 = packet.Translator.ReadBit();
            packet.Translator.ReadBit(); // fake 0
            var bit1DC = packet.Translator.ReadBit();
            packet.Translator.ReadBit(); // fake 2
            var bit2A9 = packet.Translator.ReadBit();
            var hasAttackingTarget = packet.Translator.ReadBit("Has Attacking Target", index);
            var hasStationaryPosition = packet.Translator.ReadBit("Has Stationary Position", index);
            var bit29C = packet.Translator.ReadBit();
            var bit428 = packet.Translator.ReadBit();
            var hasSceneObjectData = packet.Translator.ReadBit("Has Scene Object Data", index);
            packet.Translator.ReadBit(); // fake 1
            var bit32A = packet.Translator.ReadBit();
            var transportFrames = packet.Translator.ReadBits("Transport Frames Count", 22, index);
            var isLiving = packet.Translator.ReadBit("Is Living", index);
            var hasGameObjectPosition = packet.Translator.ReadBit("Has GameObject Position", index);
            packet.Translator.ReadBit(); // fake 3
            var hasGameObjectRotation = packet.Translator.ReadBit("Has GameObject Rotation", index);
            var bit2A4 = packet.Translator.ReadBit();
            var bit414 = packet.Translator.ReadBit();
            var hasAnimKits = packet.Translator.ReadBit("Has Anim Kits", index);

            if (isLiving)
            {
                guid1[5] = packet.Translator.ReadBit();
                packet.Translator.ReadBit("bit8D", index);
                hasPitch = !packet.Translator.ReadBit();
                guid1[6] = packet.Translator.ReadBit();
                packet.Translator.ReadBit("bitA4", index);
                bits160 = packet.Translator.ReadBits(19);
                for (var i = 0; i < bits160; ++i)
                    packet.Translator.ReadBits("bits164", 2, index, i);

                guid1[4] = packet.Translator.ReadBit();
                hasOrientation = !packet.Translator.ReadBit();
                hasMoveFlagsExtra = !packet.Translator.ReadBit();
                bitA0 = !packet.Translator.ReadBit();
                packet.Translator.StartBitStream(guid1, 2, 3, 7);
                bits90 = packet.Translator.ReadBits(22);
                hasMovementFlags = !packet.Translator.ReadBit();
                hasTimestamp = !packet.Translator.ReadBit();
                hasSplineElevation = !packet.Translator.ReadBit();
                guid1[1] = packet.Translator.ReadBit();
                hasFallData = packet.Translator.ReadBit();
                packet.Translator.ReadBit("bit8C", index);
                if (hasMoveFlagsExtra)
                    moveInfo.FlagsExtra = packet.Translator.ReadBitsE<MovementFlagExtra>("Extra Movement Flags", 13, index);

                guid1[0] = packet.Translator.ReadBit();
                moveInfo.HasSplineData = packet.Translator.ReadBit();
                if (moveInfo.HasSplineData)
                {
                    hasFullSpline = packet.Translator.ReadBit();
                    if (hasFullSpline)
                    {
                        packet.Translator.ReadBitsE<SplineFlag434>("Spline flags", 25, index);
                        hasSplineStartTime = packet.Translator.ReadBit("Has Spline Start Time", index);
                        splineCount = packet.Translator.ReadBits("SplineWaypointsCount", 20, index);
                        bit130 = packet.Translator.ReadBit();
                        if (bit130)
                        {
                            bits11C = packet.Translator.ReadBits(21);
                            packet.Translator.ReadBits("bits12C", 2, index);
                        }

                        packet.Translator.ReadBitsE<SplineMode>("Spline Mode", 2, index);
                        hasSplineVerticalAcceleration = packet.Translator.ReadBit("has Spline Vertical Acceleration", index);
                    }
                }

                hasTransportData = packet.Translator.ReadBit();
                if (hasTransportData)
                {
                    packet.Translator.StartBitStream(transportGuid, 6, 1, 2, 5);
                    hasTransportTime3 = packet.Translator.ReadBit();
                    packet.Translator.StartBitStream(transportGuid, 4, 7, 0);
                    hasTransportTime2 = packet.Translator.ReadBit();
                    packet.Translator.StartBitStream(transportGuid, 3);
                }

                if (hasMovementFlags)
                    moveInfo.Flags = packet.Translator.ReadBitsE<MovementFlag>("Movement Flags", 30, index);

                if (hasFallData)
                    hasFallDirection = packet.Translator.ReadBit();
            }

            if (hasGameObjectPosition)
            {
                hasGOTransportTime3 = packet.Translator.ReadBit();
                hasGOTransportTime2 = packet.Translator.ReadBit();
                packet.Translator.StartBitStream(goTransportGuid, 4, 2, 7, 6, 3, 0, 1, 5);
            }

            // Sceneobject data
            var CreatorGUID = new byte[8];
            byte[][] guid358 = null;
            byte[][][] guid358_6 = null;
            uint[] bits358_10 = null;
            uint[][] bits358_6_84 = null;
            uint[][] bits358_6_94 = null;
            uint[][] bits358_6_31 = null;
            uint[][] bits358_6_74 = null;
            byte[][][] bit358_6_78_9 = null;
            byte[][][] bit358_6_88_10 = null;
            bool[][][] bit358_6_88_C = null;
            bool[][][] bit358_6_88_8 = null;
            bool[][] bit358_6_2E = null;
            bool[][] bit358_6_30 = null;
            bool[] bit358_A = null;
            bool[] bit358_E = null;
            bool[] bit358_22 = null;

            var bit338 = false;
            var bit330 = false;
            var bit341 = false;
            var bit340 = false;
            var bit332 = false;
            var bit33C = false;

            uint[] bits388 = null;
            uint[] bits388_10 = null;
            byte[][] bit388_1_10 = null;
            bool[][] bit388_1_C = null;
            bool[][] bit388_1_8 = null;

            if (hasSceneObjectData)
            {
                guid358 = new byte[2][];
                guid358_6 = new byte[2][][];
                bits358_10 = new uint[2];
                bits358_6_84 = new uint[2][];
                bits358_6_94 = new uint[2][];
                bits358_6_31 = new uint[2][];
                bits358_6_74 = new uint[2][];
                bit358_6_78_9 = new byte[2][][];
                bit358_6_88_10 = new byte[2][][];
                bit358_6_88_C = new bool[2][][];
                bit358_6_88_8 = new bool[2][][];
                bit358_6_2E = new bool[2][];
                bit358_6_30 = new bool[2][];
                bit358_A = new bool[2];
                bit358_E = new bool[2];
                bit358_22 = new bool[2];

                bit332 = !packet.Translator.ReadBit();
                packet.Translator.ReadBit("bit350", index);

                for (var i = 0; i < 2; ++i)
                {
                    guid358[i] = new byte[8];
                    packet.Translator.StartBitStream(guid358[i], 5, 2);
                    bit358_A[i] = !packet.Translator.ReadBit();
                    bits358_10[i] = packet.Translator.ReadBits(2);

                    guid358_6[i] = new byte[bits358_10[i]][];
                    bits358_6_84[i] = new uint[bits358_10[i]];
                    bits358_6_94[i] = new uint[bits358_10[i]];
                    bits358_6_31[i] = new uint[bits358_10[i]];
                    bits358_6_74[i] = new uint[bits358_10[i]];
                    bit358_6_78_9[i] = new byte[bits358_10[i]][];
                    bit358_6_88_10[i] = new byte[bits358_10[i]][];
                    bit358_6_88_C[i] = new bool[bits358_10[i]][];
                    bit358_6_88_8[i] = new bool[bits358_10[i]][];
                    bit358_6_2E[i] = new bool[bits358_10[i]];
                    bit358_6_30[i] = new bool[bits358_10[i]];

                    for (var j = 0; j < bits358_10[i]; ++j)
                    {
                        guid358_6[i][j] = new byte[8];
                        bits358_6_74[i][j] = packet.Translator.ReadBits(20);
                        bits358_6_31[i][j] = packet.Translator.ReadBits(7);

                        bit358_6_78_9[i][j] = new byte[bits358_6_74[i][j]];
                        for (var k = 0; k < bits358_6_74[i][j]; ++k)
                            bit358_6_78_9[i][j][k] = (byte)(10 - packet.Translator.ReadBit());

                        guid358_6[i][j][5] = packet.Translator.ReadBit();
                        bits358_6_94[i][j] = packet.Translator.ReadBits(21);
                        packet.Translator.StartBitStream(guid358_6[i][j], 1, 3, 2, 4, 7);

                        bit358_6_2E[i][j] = !packet.Translator.ReadBit();
                        bit358_6_30[i][j] = packet.Translator.ReadBit();
                        guid358_6[i][j][0] = packet.Translator.ReadBit();

                        bits358_6_84[i][j] = packet.Translator.ReadBits(21);

                        bit358_6_88_8[i][j] = new bool[bits358_6_84[i][j]];
                        bit358_6_88_C[i][j] = new bool[bits358_6_84[i][j]];
                        bit358_6_88_10[i][j] = new byte[bits358_6_84[i][j]];
                        for (var k = 0; k < bits358_6_84[i][j]; ++k)
                        {
                            bit358_6_88_10[i][j][k] = (byte)(10 - packet.Translator.ReadBit());
                            bit358_6_88_C[i][j][k] = !packet.Translator.ReadBit();
                            bit358_6_88_8[i][j][k] = !packet.Translator.ReadBit();
                        }

                        guid358_6[i][j][6] = packet.Translator.ReadBit();
                    }

                    bit358_E[i] = !packet.Translator.ReadBit();
                    bit358_22[i] = !packet.Translator.ReadBit();
                    packet.Translator.StartBitStream(guid358[i], 4, 1, 7, 3, 6, 0);
                }

                packet.Translator.ReadBit(); // fake bit
                packet.Translator.StartBitStream(CreatorGUID, 7, 3, 2, 4, 0, 5, 6, 1);

                bits388 = new uint[3];
                bits388_10 = new uint[3];
                bit388_1_10 = new byte[3][];
                bit388_1_C = new bool[3][];
                bit388_1_8 = new bool[3][];

                for (var i = 0; i < 3; ++i)
                {
                    bits388[i] = packet.Translator.ReadBits(21);

                    bit388_1_10[i] = new byte[bits388[i]];
                    bit388_1_C[i] = new bool[bits388[i]];
                    bit388_1_8[i] = new bool[bits388[i]];
                    for (var j = 0; j < bits388[i]; ++j)
                    {
                        bit388_1_8[i][j] = !packet.Translator.ReadBit();
                        bit388_1_10[i][j] = (byte)(10 - packet.Translator.ReadBit());
                        bit388_1_C[i][j] = !packet.Translator.ReadBit();
                    }

                    bits388_10[i] = packet.Translator.ReadBits(21);
                }

                packet.Translator.ReadBit("bit351", index);
                bit340 = !packet.Translator.ReadBit();
                bit330 = !packet.Translator.ReadBit();
                bit33C = !packet.Translator.ReadBit();
                bit341 = !packet.Translator.ReadBit();
                bit338 = !packet.Translator.ReadBit();
            }

            if (bit29C)
            {
                packet.Translator.ReadBit("bit20F", index);
                bit230 = packet.Translator.ReadBit();
                bit23C = packet.Translator.ReadBit();
                packet.Translator.ReadBit("bit20C", index);
                bit228 = packet.Translator.ReadBit();
                bit218 = packet.Translator.ReadBit();
                packet.Translator.ReadBit("bit20D", index);
                packet.Translator.ReadBit("bit20E", index);
                bit298 = packet.Translator.ReadBit();
                bit220 = packet.Translator.ReadBit();
                bit284 = packet.Translator.ReadBit();
                if (bit284)
                {
                    bits26C = packet.Translator.ReadBits(21);
                    bits25C = packet.Translator.ReadBits(21);
                }

                packet.Translator.ReadBit("bit210", index);
                if (bit298)
                    bits288 = packet.Translator.ReadBits(20);

                bit258 = packet.Translator.ReadBit();
            }

            if (hasAttackingTarget)
                packet.Translator.StartBitStream(attackingTargetGuid, 4, 0, 6, 2, 1, 5, 3, 7);

            if (bit428)
                bits418 = packet.Translator.ReadBits(22);

            if (bit32A)
                bits2AA = packet.Translator.ReadBits(7);

            if (hasAnimKits)
            {
                hasAnimKit1 = !packet.Translator.ReadBit();
                hasAnimKit2 = !packet.Translator.ReadBit();
                hasAnimKit3 = !packet.Translator.ReadBit();
            }

            packet.Translator.ResetBitReader();

            for (var i = 0; i < transportFrames; ++i)
                packet.Translator.ReadInt32("Transport frame", index, i);

            if (hasGameObjectPosition)
            {
                packet.Translator.ReadSByte("GO Transport Seat", index);
                if (hasGOTransportTime2)
                    packet.Translator.ReadUInt32("GO Transport Time 2", index);

                packet.Translator.ReadXORBytes(goTransportGuid, 4, 3);
                if (hasGOTransportTime3)
                    packet.Translator.ReadUInt32("GO Transport Time 3", index);

                packet.Translator.ReadXORBytes(goTransportGuid, 7, 6, 5, 0);
                moveInfo.TransportOffset.Z = packet.Translator.ReadSingle();
                moveInfo.TransportOffset.X = packet.Translator.ReadSingle();
                packet.Translator.ReadUInt32("GO Transport Time", index);
                moveInfo.TransportOffset.O = packet.Translator.ReadSingle();
                packet.Translator.ReadXORByte(goTransportGuid, 1);
                moveInfo.TransportOffset.Y = packet.Translator.ReadSingle();
                packet.Translator.ReadXORByte(goTransportGuid, 2);

                moveInfo.TransportGuid = new WowGuid64(BitConverter.ToUInt64(goTransportGuid, 0));
                packet.AddValue("GO Transport GUID", moveInfo.TransportGuid, index);
                packet.AddValue("GO Transport Position", moveInfo.TransportOffset, index);
            }

            if (isLiving)
            {
                if (hasTransportData)
                {
                    packet.Translator.ReadXORBytes(transportGuid, 0, 5);
                    moveInfo.TransportOffset.O = packet.Translator.ReadSingle();
                    packet.Translator.ReadXORByte(transportGuid, 1);
                    moveInfo.TransportOffset.Y = packet.Translator.ReadSingle();
                    packet.Translator.ReadXORByte(transportGuid, 2);
                    packet.Translator.ReadUInt32("Transport Time", index);
                    moveInfo.TransportOffset.Z = packet.Translator.ReadSingle();
                    packet.Translator.ReadXORByte(transportGuid, 7);
                    if (hasTransportTime2)
                        packet.Translator.ReadUInt32("Transport Time 2", index);

                    packet.Translator.ReadXORBytes(transportGuid, 6, 4);
                    moveInfo.TransportOffset.X = packet.Translator.ReadSingle();
                    packet.Translator.ReadXORByte(transportGuid, 3);
                    var seat = packet.Translator.ReadSByte("Transport Seat", index);
                    if (hasTransportTime3)
                        packet.Translator.ReadUInt32("Transport Time 3", index);

                    moveInfo.TransportGuid = new WowGuid64(BitConverter.ToUInt64(transportGuid, 0));
                    packet.AddValue("Transport GUID", moveInfo.TransportGuid, index);
                    packet.AddValue("Transport Position", moveInfo.TransportOffset, index);

                    if (moveInfo.TransportGuid.HasEntry() && moveInfo.TransportGuid.GetHighType() == HighGuidType.Vehicle &&
                        guid.HasEntry() && guid.GetHighType() == HighGuidType.Creature)
                    {
                        VehicleTemplateAccessory vehicleAccessory = new VehicleTemplateAccessory
                        {
                            Entry = moveInfo.TransportGuid.GetEntry(),
                            AccessoryEntry = guid.GetEntry(),
                            SeatId = seat
                        };
                        Storage.VehicleTemplateAccessories.Add(vehicleAccessory, packet.TimeSpan);
                    }
                }

                if (moveInfo.HasSplineData)
                {
                    if (hasFullSpline)
                    {
                        if (bit130)
                        {
                            for (var i = 0; i < bits11C; ++i)
                            {
                                packet.Translator.ReadSingle("Float13C+1", index, i);
                                packet.Translator.ReadSingle("Float13C+0", index, i);
                            }
                        }

                        packet.Translator.ReadSingle("Spline Duration Multiplier Next", index);

                        for (var i = 0u; i < splineCount; ++i)
                        {
                            var wp = new Vector3
                            {
                                Y = packet.Translator.ReadSingle(),
                                Z = packet.Translator.ReadSingle(),
                                X = packet.Translator.ReadSingle()
                            };

                            packet.AddValue("Spline Waypoint", wp, index, i);
                        }

                        packet.Translator.ReadInt32("Spline Time", index); // if need swap with "Spline Full Time"
                        var type = packet.Translator.ReadByte();
                        switch (type)
                        {
                            case 1:
                                splineType = SplineType.Normal;
                                break;
                            case 2:
                                splineType = SplineType.FacingSpot;
                                break;
                            case 3:
                                splineType = SplineType.FacingTarget;
                                break;
                            case 4:
                                splineType = SplineType.FacingAngle;
                                break;
                        }

                        if (splineType == SplineType.FacingAngle)
                            packet.Translator.ReadSingle("Facing Angle", index);

                        packet.Translator.ReadSingle("Spline Duration Multiplier", index); // if need swap with "Spline Duration Multiplier Next"
                        if (hasSplineStartTime)
                            packet.Translator.ReadInt32("Spline Start Time", index);

                        if (hasSplineVerticalAcceleration)
                            packet.Translator.ReadSingle("Spline Vertical Acceleration", index);

                        if (splineType == SplineType.FacingSpot)
                        {
                            var point = new Vector3
                            {
                                Y = packet.Translator.ReadSingle(),
                                Z = packet.Translator.ReadSingle(),
                                X = packet.Translator.ReadSingle()
                            };

                            packet.AddValue("Facing Spot", point, index);
                        }

                        packet.Translator.ReadInt32("Spline Full Time", index);
                    }

                    packet.Translator.ReadInt32("Spline Id", index);
                    moveInfo.Position.Y = packet.Translator.ReadSingle();
                    moveInfo.Position.Z = packet.Translator.ReadSingle();
                    moveInfo.Position.X = packet.Translator.ReadSingle();
                }

                moveInfo.Position.Y = packet.Translator.ReadSingle();
                packet.Translator.ReadXORByte(guid1, 7);
                for (var i = 0; i < bits160; ++i)
                {
                    packet.Translator.ReadSingle("Float14", index, i);
                    packet.Translator.ReadUInt32("Int0", index, i);
                    packet.Translator.ReadUInt32("Int10", index, i);
                    packet.Translator.ReadSingle("Float4", index, i);
                    packet.Translator.ReadSingle("Float8", index, i);
                    packet.Translator.ReadSingle("FloatC", index, i);
                }

                if (hasFallData)
                {
                    packet.Translator.ReadUInt32("Time Fallen", index);
                    if (hasFallDirection)
                    {
                        packet.Translator.ReadSingle("Jump Sin", index);
                        packet.Translator.ReadSingle("Jump Cos", index);
                        packet.Translator.ReadSingle("Jump Velocity", index);
                    }

                    packet.Translator.ReadSingle("Fall Start Velocity", index);
                }

                if (hasSplineElevation)
                    packet.Translator.ReadSingle("Spline Elevation", index);

                if (bitA0)
                    packet.Translator.ReadInt32("Int98", index);

                packet.Translator.ReadXORByte(guid1, 1);
                packet.Translator.ReadSingle("Turn Speed", index);
                packet.Translator.ReadSingle("FlyBack Speed", index);
                packet.Translator.ReadSingle("RunBack Speed", index);

                if (hasTimestamp)
                    packet.Translator.ReadUInt32("Time", index);

                moveInfo.Position.X = packet.Translator.ReadSingle();
                packet.Translator.ReadXORByte(guid1, 2);
                packet.Translator.ReadSingle("Swim Speed", index);
                packet.Translator.ReadSingle("SwimBack Speed", index);

                if (hasOrientation)
                    moveInfo.Orientation = packet.Translator.ReadSingle();

                packet.Translator.ReadSingle("Fly Speed", index);
                packet.Translator.ReadXORByte(guid1, 6);

                if (hasPitch)
                    packet.Translator.ReadSingle("Pitch", index);

                moveInfo.RunSpeed = packet.Translator.ReadSingle("Run Speed", index) / 7.0f;
                packet.Translator.ReadSingle("Pitch Speed", index);
                packet.Translator.ReadXORBytes(guid1, 0, 5);

                for (var i = 0; i < bits90; ++i)
                    packet.Translator.ReadInt32("Int8C", index, i);

                packet.Translator.ReadXORByte(guid1, 4);
                moveInfo.WalkSpeed = packet.Translator.ReadSingle("Walk Speed", index) / 2.5f;
                moveInfo.Position.Z = packet.Translator.ReadSingle();
                packet.Translator.ReadXORByte(guid1, 3);

                packet.Translator.WriteGuid("GUID1", guid1, index);
                packet.AddValue("Position", moveInfo.Position, index);
                packet.AddValue("Orientation", moveInfo.Orientation, index);
            }

            if (hasSceneObjectData)
            {
                for (var i = 0; i < 2; ++i)
                {
                    if (bit358_22[i])
                        packet.Translator.ReadByte("byte358+22", index, i);

                    packet.Translator.ReadByte("byte358+23", index, i);

                    for (var j = 0; j < bits358_10[i]; ++j)
                    {
                        packet.Translator.ReadXORByte(guid358_6[i][j], 7);
                        packet.Translator.ReadInt32("int358+6+1C", index, i, j);
                        packet.Translator.ReadWoWString("string358+6+31", (int)bits358_6_31[i][j], index, i, j);

                        for (var k = 0; k < bits358_6_74[i][j]; ++k)
                        {
                            packet.Translator.ReadInt16("short358+6+78+6", index, i, j, k);
                            packet.Translator.ReadInt32("int358+6+78+0", index, i, j, k);
                            if (bit358_6_78_9[i][j][k] != 9)
                                packet.Translator.ReadByte("byte358+6+78+9", index, i, j, k);

                            packet.Translator.ReadInt16("short358+6+78+4", index, i, j, k);
                            packet.Translator.ReadByte("byte358+6+78+8", index, i, j, k);
                        }

                        for (var k = 0; k < bits358_6_94[i][j]; ++k)
                        {
                            packet.Translator.ReadInt32("int358+6+98+4", index, i, j, k);
                            packet.Translator.ReadInt32("int358+6+98+0", index, i, j, k);
                        }

                        packet.Translator.ReadXORByte(guid358_6[i][j], 6);
                        packet.Translator.ReadInt16("short358+6+2C", index, i, j);

                        if (bit358_6_2E[i][j])
                            packet.Translator.ReadInt16("short358+6+2E", index, i, j);

                        for (var k = 0; k < bits358_6_84[i][j]; ++k)
                        {
                            if (bit358_6_88_10[i][j][k] != 9)
                                packet.Translator.ReadByte("byte358+6+88+10", index, i, j, k);

                            if (bit358_6_88_8[i][j][k])
                                packet.Translator.ReadInt32("int358+6+88+8", index, i, j, k);

                            packet.Translator.ReadInt32("int358+6+88+0", index, i, j, k);
                            packet.Translator.ReadInt32("int358+6+88+4", index, i, j, k);

                            if (bit358_6_88_C[i][j][k])
                                packet.Translator.ReadInt32("int358+6+88+C", index, i, j, k);

                        }

                        packet.Translator.ReadInt16("short358+6+16", index, i, j);
                        packet.Translator.ReadInt32("int358+6+8", index, i, j);
                        packet.Translator.ReadInt16("short358+6+14", index, i, j);
                        packet.Translator.ReadInt32("int358+6+24", index, i, j);

                        if (!bit358_6_30[i][j])
                            packet.Translator.ReadByte("byte358+6+30", index, i, j);

                        packet.Translator.ReadInt32("int358+6+10", index, i, j);
                        packet.Translator.ReadXORByte(guid358_6[i][j], 3);
                        packet.Translator.ReadInt32("int358+6+28", index, i, j);
                        packet.Translator.ReadInt32("int358+6+18", index, i, j);
                        packet.Translator.ReadXORBytes(guid358_6[i][j], 0, 2, 4, 5);
                        packet.Translator.ReadInt32("int358+6+C", index, i, j);
                        packet.Translator.ReadInt32("int358+6+20", index, i, j);
                        packet.Translator.ReadXORByte(guid358_6[i][j], 1);

                        packet.Translator.WriteGuid("Guid 358_6", guid358_6[i][j], index, i, j);
                    }

                    packet.Translator.ReadXORBytes(guid358[i], 7, 1, 4);

                    if (bit358_E[i])
                        packet.Translator.ReadInt16("short358+E", index, i);

                    if (bit358_A[i])
                        packet.Translator.ReadInt32("int358+A", index, i);

                    packet.Translator.ReadXORBytes(guid358[i], 0, 5, 2, 3);
                    packet.Translator.ReadInt32("int358+8", index, i);
                    packet.Translator.ReadXORByte(guid358[i], 6);
                    packet.Translator.WriteGuid("Guid358", guid358[i], index, i);
                }

                for (var i = 0; i < 3; ++i)
                {

                    for (var j = 0; j < bits388[i]; ++j)
                    {
                        if (bit388_1_C[i][j])
                            packet.Translator.ReadInt32("int388+1+C", index, i, j);

                        packet.Translator.ReadInt32("int388+1+4", index, i, j);
                        packet.Translator.ReadInt32("int388+1+0", index, i, j);

                        if (bit388_1_10[i][j] != 9)
                            packet.Translator.ReadByte("byte388+1+10", index, i, j);

                        if (bit388_1_8[i][j])
                            packet.Translator.ReadInt32("int388+1+8", index, i, j);
                    }

                    for (var j = 0; j < bits388_10[i]; ++j)
                    {
                        packet.Translator.ReadInt32("int388+6+4", index, i, j);
                        packet.Translator.ReadInt32("int388+6+0", index, i, j);
                    }
                }

                packet.Translator.ReadInt32("Int334", index);
                packet.Translator.ParseBitStream(CreatorGUID, 1, 6, 0, 5, 7, 4, 3, 2);

                if (bit340)
                    packet.Translator.ReadByte("Byte340", index);

                if (bit341)
                    packet.Translator.ReadByte("byte341", index);

                if (bit338)
                    packet.Translator.ReadInt32("int338", index);

                if (bit332)
                    packet.Translator.ReadInt16("Short332", index);

                if (bit33C)
                    packet.Translator.ReadInt32("int33C", index);

                if (bit330)
                    packet.Translator.ReadInt16("Short318", index);

                packet.Translator.WriteGuid("Creator GUID", CreatorGUID, index);
            }

            if (hasStationaryPosition)
            {
                moveInfo.Position.X = packet.Translator.ReadSingle();
                moveInfo.Position.Z = packet.Translator.ReadSingle();
                moveInfo.Position.Y = packet.Translator.ReadSingle();
                moveInfo.Orientation = packet.Translator.ReadSingle("Stationary Orientation", index);

                packet.AddValue("Stationary Position", moveInfo.Position, index);
            }

            if (hasAttackingTarget)
            {
                packet.Translator.ParseBitStream(attackingTargetGuid, 1, 3, 5, 4, 7, 6, 2, 0);
                packet.Translator.WriteGuid("Attacking GUID", attackingTargetGuid, index);
            }

            if (bit29C)
            {
                if (bit230)
                    packet.Translator.ReadInt32("int22C", index);

                if (bit258)
                {
                    packet.Translator.ReadSingle("Float240", index);
                    packet.Translator.ReadSingle("Float24C", index);
                    packet.Translator.ReadSingle("Float250", index);
                    packet.Translator.ReadSingle("Float244", index);
                    packet.Translator.ReadSingle("Float254", index);
                    packet.Translator.ReadSingle("Float248", index);
                }

                packet.Translator.ReadInt32("Int208", index);

                if (bit23C)
                {
                    packet.Translator.ReadSingle("Float234", index);
                    packet.Translator.ReadSingle("Float238", index);
                }

                if (bit220)
                    packet.Translator.ReadInt32("int21C", index);

                if (bit284)
                {
                    packet.Translator.ReadSingle("Float27C", index);
                    packet.Translator.ReadSingle("Float280", index);

                    for (var i = 0; i < bits25C; ++i)
                    {
                        packet.Translator.ReadSingle("Float260+0", index, i);
                        packet.Translator.ReadSingle("Float260+1", index, i);
                    }

                    for (var i = 0; i < bits26C; ++i)
                    {
                        packet.Translator.ReadSingle("Float270+1", index, i);
                        packet.Translator.ReadSingle("Float270+0", index, i);
                    }
                }

                if (bit218)
                    packet.Translator.ReadInt32("int214", index);

                if (bit298)
                {
                    for (var i = 0; i < bits288; ++i)
                    {
                        packet.Translator.ReadSingle("Float28C+0", index, i);
                        packet.Translator.ReadSingle("Float28C+1", index, i);
                        packet.Translator.ReadSingle("Float28C+2", index, i);
                    }
                }

                if (bit228)
                    packet.Translator.ReadInt32("int224", index);
            }

            if (bit1DC)
                packet.Translator.ReadInt32("int1D8", index);

            if (hasAnimKits)
            {
                if (hasAnimKit2)
                    packet.Translator.ReadUInt16("Anim Kit 2", index);
                if (hasAnimKit3)
                    packet.Translator.ReadUInt16("Anim Kit 3", index);
                if (hasAnimKit1)
                    packet.Translator.ReadUInt16("Anim Kit 1", index);
            }


            if (hasGameObjectRotation)
                moveInfo.Rotation = packet.Translator.ReadPackedQuaternion("GameObject Rotation", index);

            if (hasVehicleData)
            {
                moveInfo.VehicleId = packet.Translator.ReadUInt32("Vehicle Id", index);
                packet.Translator.ReadSingle("Vehicle Orientation", index);
            }

            if (bit32A)
                packet.Translator.ReadBytes("Bytes", (int)bits2AA);

            if (bit414)
                packet.Translator.ReadInt32("int410", index);

            if (bit2A4)
                packet.Translator.ReadInt32("int2A0", index);

            if (bit428)
                for (var i = 0; i < bits418; ++i)
                    packet.Translator.ReadInt32("Int3F8", index, i);

            if (isLiving && moveInfo.HasSplineData && hasFullSpline && splineType == SplineType.FacingTarget)
            {
                var facingTargetGuid = new byte[8];
                facingTargetGuid = packet.Translator.StartBitStream(5, 0, 2, 4, 1, 3, 6, 7);
                packet.Translator.ParseBitStream(facingTargetGuid, 5, 0, 4, 6, 3, 2, 1, 7);
                packet.Translator.WriteGuid("Facing Target GUID", facingTargetGuid, index);
            }

            return moveInfo;
        }

        [Parser(Opcode.SMSG_DESTROY_OBJECT)]
        public static void HandleDestroyObject(Packet packet)
        {
            var guid = new byte[8];

            guid[5] = packet.Translator.ReadBit();
            guid[6] = packet.Translator.ReadBit();
            guid[1] = packet.Translator.ReadBit();
            guid[7] = packet.Translator.ReadBit();
            guid[2] = packet.Translator.ReadBit();
            guid[0] = packet.Translator.ReadBit();
            guid[3] = packet.Translator.ReadBit();
            packet.Translator.ReadBit("Despawn Animation");
            guid[4] = packet.Translator.ReadBit();

            packet.Translator.ParseBitStream(guid, 1, 5, 3, 0, 2, 6, 7, 4);

            packet.Translator.WriteGuid("GUID", guid);
        }

        [Parser(Opcode.CMSG_OBJECT_UPDATE_FAILED)]
        public static void HandleObjectUpdateFailed(Packet packet)
        {
            var guid = packet.Translator.StartBitStream(5, 4, 2, 7, 0, 6, 3, 1);
            packet.Translator.ParseBitStream(guid, 6, 3, 0, 7, 1, 4, 2, 5);
            packet.Translator.WriteGuid("Guid", guid);
        }
    }
}
