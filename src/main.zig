const std = @import("std");
const ds = @import("ds.zig");
const inter = @import("inter.zig");
const time = @import("time.zig");

pub fn main() void {
    std.debug.print("gettin ziggy wit it", .{});
}

const Query = struct { time: time.Type, limit: u64 };

fn LocalReplica(comptime Payload: type) type {
    return struct {
        const maxNumEvents = 10_000;

        const Log = std.ArrayListUnmanaged(inter.Event(Payload));

        const IdIndex = std.AutoHashMapUnmanaged(inter.EventId, u64);

        // The log is the source of truth. Everything else is just a cache!
        log: Log,
        // This is an "operational index" - needed for the replica to work, but
        // not the source of truth
        id_index: IdIndex,
        // Each replica should have its own storage
        allocator: std.mem.Allocator,

        fn init(allocator: std.mem.Allocator) !@This() {
            var id_index: IdIndex = .{};
            try id_index.ensureTotalCapacity(allocator, maxNumEvents);
            return .{
                .log = try Log.initCapacity(allocator, maxNumEvents),
                .id_index = id_index,
                .allocator = allocator,
            };
        }

        fn deinit(self: *@This()) void {
            self.log.deinit(self.allocator);
            self.id_index.deinit(self.allocator);
        }

        fn read(self: @This(), query: Query) []const inter.Event(Payload) {
            _ = query;
            _ = self;
        }
    };
}

test "BST" {
    var arena = std.heap.ArenaAllocator.init(std.testing.allocator);
    defer arena.deinit();
    var bst = ds.BST(u64, u64).init();

    try std.testing.expectEqual(@as(usize, 0), bst.len);

    // TODO: some kind of 'from iterator' ? how do std lib containers do it?
    try bst.put(arena.allocator(), 4, 2);
    try bst.put(arena.allocator(), 0, 1);
    try bst.put(arena.allocator(), 10, 0);
    try bst.put(arena.allocator(), 2, 20);

    try std.testing.expectEqual(@as(usize, 4), bst.len);
    try std.testing.expectEqual(@as(?u64, 2), bst.get(4));
    try std.testing.expectEqual(@as(?u64, null), bst.get(27));

    // Try and see what the shape of the tree is
    try std.testing.expectEqual(@as(u64, 2), bst.root.?.val);
    try std.testing.expectEqual(@as(u64, 1), bst.root.?.left.?.val);
    try std.testing.expectEqual(@as(u64, 0), bst.root.?.right.?.val);

    var iter = try bst.iterator(arena.allocator());
    defer iter.deinit(arena.allocator());

    try std.testing.expectEqual(@as(u64, 0), iter.next().?.key_ptr.*);
    try std.testing.expectEqual(@as(u64, 2), iter.next().?.key_ptr.*);
    try std.testing.expectEqual(@as(u64, 4), iter.next().?.key_ptr.*);
    try std.testing.expectEqual(@as(u64, 10), iter.next().?.key_ptr.*);
}

test "Logical Clock" {
    var lc = try time.LogicalClock.init(std.testing.allocator);
    defer lc.deinit(std.testing.allocator);
}

test "Local Replica" {
    var r = try LocalReplica(u64).init(std.testing.allocator);
    defer r.deinit();
}
