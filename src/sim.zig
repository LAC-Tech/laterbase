const std = @import("std");
const ds = @import("ds.zig");
const net = @import("net.zig");
const time = @import("time.zig");

pub fn main() void {
    std.debug.print("gettin ziggy wit it", .{});
}

const expectEqual = std.testing.expectEqual;

test {
    _ = ExampleTests;
}

const SeededTests = struct {
    rng: std.rand.Xoshiro256,

    fn init(seed: u64) @This() {
        return .{ .rng = std.rand.DefaultPrng.init(seed) };
    }
};

const ExampleTests = struct {
    test "Logical Clock" {
        var lc = try time.LogicalClock.init(std.testing.allocator);
        defer lc.deinit(std.testing.allocator);
    }
};

test "Local Replica" {
    //var addr_bytes = [_]u8{0} ** 16;
    //var addr = net.Address{ .bytes = &addr_bytes };
    //var r = try LocalReplica(u8).init(std.testing.allocator, addr);
    //defer r.deinit();

    //var events = std.ArrayList(net.Event(u8)).init(std.testing.allocator);
    //defer events.deinit();

    //try r.read(std.ArrayList, &events, .{ .logical_txn = 0 });

    //try expectEqual(@as(u64, 0), events.items.len);
    //try r.read(std.ArrayList, &events, .{ .logical_txn = 0 });
}
