﻿using WowPacketParser.Enums;
using WowPacketParser.Misc;
using WowPacketParser.Parsing;

namespace WowPacketParserModule.V5_4_7_17898.Parsers
{
    public static class LfgHandler
    {
        [Parser(Opcode.SMSG_LFG_PLAYER_INFO)]
        public static void HandleLfgPlayerLockInfoResponse(Packet packet)
        {
            var guid = new byte[8];

            var bit18 = packet.Translator.ReadBit();
            if (bit18)
                packet.Translator.StartBitStream(guid, 0, 6, 7, 5, 2, 4, 1, 3);

            var bits30 = packet.Translator.ReadBits(17);

            var bits7C = new uint[bits30];
            var bits6C = new uint[bits30];
            var bits40 = new uint[bits30];
            var bits1C = new uint[bits30][];
            var bits2C = new uint[bits30][];
            var bitsC = new uint[bits30][];
            var bit4 = new bool[bits30];
            var bits5C = new uint[bits30];
            var bit3C = new bool[bits30];

            for (var i = 0; i < bits30; i++)
            {
                bits7C[i] = packet.Translator.ReadBits(21);
                bits6C[i] = packet.Translator.ReadBits(21);
                bits40[i] = packet.Translator.ReadBits(19);

                bits1C[i] = new uint[bits40[i]];
                bits2C[i] = new uint[bits40[i]];
                bitsC[i] = new uint[bits40[i]];

                for (var j = 0; j < bits40[i]; j++)
                {
                    bits1C[i][j] = packet.Translator.ReadBits(21);
                    bits2C[i][j] = packet.Translator.ReadBits(21);
                    bitsC[i][j] = packet.Translator.ReadBits(20);
                }

                bit3C[i] = packet.Translator.ReadBit();
                bit4[i] = packet.Translator.ReadBit();
                bits5C[i] = packet.Translator.ReadBits(20);
            }

            var bits20 = packet.Translator.ReadBits(20);
            for (var i = 0; i < bits30; i++)
            {

                for (var j = 0; j < bits40[i]; j++)
                {
                    for (var k = 0; k < bitsC[i][j]; k++)
                    {
                        packet.Translator.ReadInt32("Int0", i, j, k);
                        packet.Translator.ReadInt32("Int34", i, j, k);
                        packet.Translator.ReadInt32("Int34", i, j, k);
                    }

                    for (var k = 0; k < bits2C[i][j]; k++)
                    {
                        packet.Translator.ReadInt32("Int34", i, j, k);
                        packet.Translator.ReadInt32("Int34", i, j, k);
                    }

                    for (var k = 0; k < bits1C[i][j]; k++)
                    {
                        packet.Translator.ReadInt32("Int34", i, j, k);
                        packet.Translator.ReadInt32("Int34", i, j, k);
                    }

                    packet.Translator.ReadInt32("Int44+8", i, j);
                    packet.Translator.ReadInt32("Int44+0", i, j);
                    packet.Translator.ReadInt32("Int44+4", i, j);
                }

                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);

                for (var j = 0; j < bits7C[i]; j++)
                {
                    packet.Translator.ReadInt32("Int34", i, j);
                    packet.Translator.ReadInt32("Int34", i, j);
                }

                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);

                for (var j = 0; j < bits5C[i]; j++)
                {
                    packet.Translator.ReadInt32("Int34", i, j);
                    packet.Translator.ReadInt32("Int0", i, j);
                    packet.Translator.ReadInt32("Int34", i, j);
                }

                for (var j = 0; j < bits6C[i]; j++)
                {
                    packet.Translator.ReadInt32("Int34", i, j);
                    packet.Translator.ReadInt32("Int34", i, j);
                }

                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
                packet.Translator.ReadInt32("Int34", i);
            }

            if (bit18)
            {
                packet.Translator.ParseBitStream(guid, 6, 3, 0, 4, 5, 1, 2, 7);
                packet.Translator.WriteGuid("Guid", guid);
            }

            for (var i = 0; i < bits20; i++)
            {
                packet.Translator.ReadInt32("IntED", i);
                packet.Translator.ReadInt32("Int24", i);
                packet.Translator.ReadInt32("IntED", i);
                packet.Translator.ReadInt32("IntED", i);
            }
        }

        [Parser(Opcode.SMSG_LFG_QUEUE_STATUS)]
        public static void HandleLfgQueueStatusUpdate434(Packet packet)
        {
            var guid = new byte[8];

            packet.Translator.ReadInt32("Int30");
            packet.Translator.ReadInt32("Int28");
            packet.Translator.ReadInt32("Int10");
            packet.Translator.ReadInt32("Int1C");

            for (var i = 0; i < 3; ++i)
            {
                packet.Translator.ReadByte("Byte44", i);
                packet.Translator.ReadInt32("Int2C", i);

            }

            packet.Translator.ReadInt32("Int2C");
            packet.Translator.ReadInt32("Int18");
            packet.Translator.ReadInt32("Int14");

            packet.Translator.StartBitStream(guid, 2, 0, 6, 5, 1, 4, 7, 3);
            packet.Translator.ParseBitStream(guid, 6, 1, 2, 4, 7, 3, 5, 0);

            packet.Translator.WriteGuid("Guid", guid);
        }
    }
}
