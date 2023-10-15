const std = @import("std");
const Address = @import("msg").Address;

// type LogicalClock private (dict:OrderedDict<Address, uint64<counter>>) =
//     let seq() = Seq.map (fun (k, v) -> (k, v)) dict

//     new() = LogicalClock(OrderedDict())

//     member _.Update(fromAddr, newCounter) =
//         let newCounter =
//             match dict.Get(fromAddr) with
//             | Some counter -> max counter newCounter
//             | None -> 0UL<counter>

//         dict.OverWrite(fromAddr, newCounter)

//     member _.Get(addr) = dict.GetOrDefault(addr, 0UL<counter>)

//     interface IEnumerable<Address * uint64<counter>> with
//         member _.GetEnumerator(): IEnumerator<_> = seq().GetEnumerator()
//         member _.GetEnumerator(): Collections.IEnumerator =
//             seq().GetEnumerator()

// Logical counter that tracks a position in a replicas log.
const Counter = packed struct {
    raw: u64,
    const zero: @This() = .{ .raw = 0 };
    // TODO: is this even needed?
    fn merge(self: *@This(), counter: @This()) void {
        self.raw = @max(self.raw, counter.raw);
    }
};

// Essentially a version vector
pub const LogicalClock = struct {
    const Counters = std.AutoHashMapUnmanaged([]const u8, Counter);

    counters: Counters,

    pub fn init(allocator: std.mem.Allocator) @This() {
        return .{ .counters = Counters.init(allocator) };
    }

    pub fn deinit(self: *@This(), allocator: std.mem.Allocator) void {
        self.counters.deinit(allocator);
    }

    pub fn get(self: @This(), addr: Address) Counter {
        return self.counters.get(addr) orelse return 0;
    }

    pub fn put(self: *@This(), addr: Address, counter: Counter) !void {
        var entry = try self.store.getOrPut(addr.bytes);
        if (entry.found_existing) {
            entry.value_ptr.merge(counter);
        } else {
            entry.value_ptr.* = Counter.zero;
        }
    }
};
