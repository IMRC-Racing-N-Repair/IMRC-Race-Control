using System.Text;
using IMRCRaceControl.Core;
using IMRCRaceControl.Services;

internal static class TrackmateChecks
{
    public static async Task<int> Run()
    {
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("Trackmate: " + label); count++; }
        var now = new DateTimeOffset(2026,10,10,12,0,0,TimeSpan.Zero);
        byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);
        // Constructed from the documented RC field positions; not a hardware capture.
        var first = Bytes("\u0001@\t202\t1\t0001234\t10.000\t7\r\n");
        var second = Bytes("\u0001@\t202\t2\t0001234\t14.339\t9\r\n");
        Check(Convert.ToHexString(TrackmateProtocol.StartCommand)=="013F2C3230322C302C31312C0D0A", "RC startup wire bytes");
        Check(TrackmateProtocol.BaudRate==9600,"RC baud preset");
        for (var split=1; split<first.Length; split++)
        {
            var reader = new AutomaticDecoder(true);
            var passings = reader.Feed(first.AsSpan(0,split),now).Passings.Concat(reader.Feed(first.AsSpan(split),now).Passings).ToList();
            Check(passings.Count==1 && passings[0].TransponderId=="0001234", "every split preserves full ID: " + split);
        }
        var decoder = new AutomaticDecoder(true);
        var decoded = new List<DecoderPassing>();
        foreach(var b in first) decoded.AddRange(decoder.Feed(new[]{b},now).Passings);
        Check(decoded.Count==1 && decoded[0].ClockMicroseconds==10000000 && decoded[0].Hits==7,"byte-by-byte passing and clock/hits");
        Check(decoder.Feed(first,now).Passings.Count==0,"exact retransmission suppressed");
        var next = decoder.Feed(second,now.AddSeconds(5)).Passings.Single();
        Check(next.Timestamp-decoded[0].Timestamp==TimeSpan.FromMilliseconds(4339),"hardware lap interval independent of delayed reads");
        var combined = new AutomaticDecoder(true).Feed(first.Concat(second).ToArray(),now).Passings;
        Check(combined.Count==2 && combined[1].Timestamp-combined[0].Timestamp==TimeSpan.FromMilliseconds(4339),"batched passings preserve interval");
        Check(new AutomaticDecoder().Feed(first,now).Passings.Count==0,"Trackmate grammar not guessed on generic serial");
        foreach(var bad in new[]{"1234\r\n","@\t202\t1\t1234\t1\r\n","\u0001@\t202\t0001234\t10.0\r\n",
            "\u0001#\t202\t1\t1234\t5\r\n","\u0001%\t202\t0\t0\r\n","\u0001@\t202\t1\t0\t10.0\r\n",
            "\u0001@\t202\t1\t1234\t-1\r\n","\u0001@\t202\t1\t1234\tNaN\r\n","\u0001@\t202\t1\t1234\t99999999999\r\n",
            "\u0001@\t202\t1\t4294967296\t10\r\n","\u0001@\t202\t1\t1234\t10\tbad\r\n"})
            Check(new AutomaticDecoder(true).Feed(Bytes(bad),now).Passings.Count==0,"noise, status and malformed records do not score");
        var recover = new AutomaticDecoder(true);
        Check(recover.Feed(Bytes("\u0001@"+new string('9',2000)+"\r\n").Concat(first).ToArray(),now).Passings.Count==1,"oversized input recovers at next frame");
        recover.ResetConnection(); recover.Feed(first.AsSpan(0,10),now); recover.ResetConnection();
        Check(recover.Feed(first.AsSpan(10),now).Passings.Count==0,"partial data cannot span reconnect");
        Check(recover.Feed(first,now).Passings.Count==1,"startup clock reset may repeat first ID and timestamp");
        var restarted=Bytes("\u0001@\t202\t1\t0001234\t0.100\t7\r\n");
        Check(recover.Feed(restarted,now.AddSeconds(20)).Passings.Single().Timestamp==now.AddSeconds(20),"backward device clock reanchors after restart");
        var twoCars=new AutomaticDecoder(true).Feed(first.Concat(Bytes("\u0001@\t202\t2\t0005678\t10.000\t7\r\n")).ToArray(),now).Passings;
        Check(twoCars.Count==2 && twoCars[0].Timestamp==twoCars[1].Timestamp,"simultaneous cars are independent");
        var packets=new List<DecoderIngressService.FormattedPacket>();
        await using(var ingress=new DecoderIngressService())
        {
            ingress.PacketFormatted += p=>packets.Add(p);
            ingress.Enqueue("Trackmate COM3",first[..10],now);
            ingress.Enqueue("Trackmate COM3",first[10..],now);
            ingress.ResetConnection();
            ingress.Enqueue("Trackmate COM3",first,now.AddSeconds(1));
            ingress.Enqueue("USB/Serial COM3",second,now.AddSeconds(5));
        }
        Check(packets.SelectMany(p=>p.Passings).Count()==2,"ingress routes explicit profile and handles reconnect");
        Check(packets[1].Protocol=="Trackmate" && packets[1].Passings.Single().TransponderId=="0001234","normalized crossing reaches UI pipeline");
        Check(packets[0].Hex==Convert.ToHexString(first[..10]),"raw diagnostic traffic retained");
        return count;
    }
}
