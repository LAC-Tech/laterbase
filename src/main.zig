const std = @import("std");
const ds = @import("ds.zig");
const net = @import("net.zig");
const time = @import("time.zig");

const expectEqual = std.testing.expectEqual;

pub fn main() void {
    std.debug.print("gettin ziggy wit it", .{});
}

const Query = union(time.Type) {
    logical_txn: u64,
    physical_valid: i64,
};

fn LocalReplica(comptime Payload: type) type {
    const Event = net.Event(Payload);
    const Msg = net.Message(Payload);
    return struct {
        const maxNumEvents = 10_000;

        const Log = std.ArrayListUnmanaged(Event);
        const IdIndex = ds.BST(net.EventId, u64, net.EventId.order);

        // The log is the source of truth. Everything else is just a cache!
        log: Log,
        // This is an "operational index" - needed for a replica to work.
        // Used to prevent adding duplicates to the log, and for read queries
        // It is not a source of truth - only the log is
        id_index: IdIndex,
        logical_clock: time.LogicalClock,
        addr: net.Address,
        // Each replica should have its own storage
        send: *const fn (msg: Msg, dest_addr: net.Address) void,
        allocator: std.mem.Allocator,

        fn init(allocator: std.mem.Allocator, addr: net.Address) !@This() {
            return .{
                .log = try Log.initCapacity(allocator, maxNumEvents),
                .id_index = try IdIndex.init(allocator, maxNumEvents),
                .addr = addr,
                .allocator = allocator,
            };
        }

        fn deinit(self: *@This()) void {
            self.log.deinit(self.allocator);
            self.id_index.deinit();
        }

        fn read(
            self: @This(),
            comptime Buf: fn (comptime type) type,
            buf: *Buf(Event),
            query: Query,
        ) !void {
            switch (query) {
                .logical_txn => |n| {
                    var events = self.log.items[n..];
                    try buf.appendSlice(events);
                },
                .physical_valid => |n| {
                    var prefix_key = net.EventId.init(n, 0);
                    var it = try self.id_index.iteratorFrom(
                        self.allocator,
                        prefix_key,
                    );
                    defer it.deinit(self.allocator);

                    while (it.next()) |entry| {
                        var index = entry.value_ptr.*;
                        try buf.append(self.log.items[index]);
                    }
                },
            }
        }

        // fn recv(self: *@This(), msg: Msg, comptime send_msg: anytype) void {
        //     // TODO: one buffer for whole replica? use different allocation?
        //     var msg_event_buf = std.ArrayList(Event).init(self.allocator);
        //     defer msg_event_buf.deinit();

        //     switch (msg) {
        //         .replicate_from => |dest_addr| {
        //             self.send_msg(
        //                 .{
        //                     .src = self.addr,
        //                     .counter = self.logical_clock.get(dest_addr),
        //                 },
        //                 dest_addr,
        //             );
        //         },
        //         .send_since => |value| {
        //             const events_to_send = self.log.items[value.counter.raw..];
        //             msg_event_buf.appendSlice(events_to_send);

        //             send_msg(.{
        //                 .events = msg_event_buf.toOwnedSlice(),
        //                 .until = .{ .raw = self.log.slice.len },
        //                 .src = self.addr,
        //             }, value.src);
        //         },
        //         else => @panic("TODO: implement me"),
        //     }
        // }
    };
}

test "BST" {
    var bst = try ds.BST(u64, u64, std.math.order).init(std.testing.allocator, 8);
    defer bst.deinit();

    try std.testing.expectEqual(@as(usize, 0), bst.len);

    // TODO: some kind of 'from iterator' ? how do std lib containers do it?
    try bst.put(4, 2);
    try bst.put(0, 1);
    try bst.put(10, 0);
    try bst.put(2, 20);

    try expectEqual(@as(usize, 4), bst.len);
    try expectEqual(@as(?u64, 2), bst.get(4));
    try expectEqual(@as(?u64, null), bst.get(27));

    // Try and see what the shape of the tree is
    try expectEqual(@as(u64, 2), bst.root.?.val);
    try expectEqual(@as(u64, 1), bst.root.?.left.?.val);
    try expectEqual(@as(u64, 0), bst.root.?.right.?.val);

    var iter = try bst.iterator(std.testing.allocator);
    defer iter.deinit(std.testing.allocator);
    try expectEqual(@as(u64, 0), iter.next().?.key_ptr.*);
    try expectEqual(@as(u64, 2), iter.next().?.key_ptr.*);
    try expectEqual(@as(u64, 4), iter.next().?.key_ptr.*);
    try expectEqual(@as(u64, 10), iter.next().?.key_ptr.*);

    var iter2 = try bst.iteratorFrom(std.testing.allocator, 1);
    defer iter2.deinit(std.testing.allocator);
    try expectEqual(@as(u64, 2), iter2.next().?.key_ptr.*);
    try expectEqual(@as(u64, 4), iter2.next().?.key_ptr.*);
    try expectEqual(@as(u64, 10), iter2.next().?.key_ptr.*);

    var iter3 = try bst.iteratorFrom(std.testing.allocator, 3);
    defer iter3.deinit(std.testing.allocator);
    try expectEqual(@as(u64, 4), iter3.next().?.key_ptr.*);
    try expectEqual(@as(u64, 10), iter3.next().?.key_ptr.*);
}

test "Logical Clock" {
    var lc = try time.LogicalClock.init(std.testing.allocator);
    defer lc.deinit(std.testing.allocator);
}

test "Local Replica" {
    var r = try LocalReplica(u8).init(std.testing.allocator);
    defer r.deinit();

    // var events = std.ArrayList(net.Event(u8)).init(std.testing.allocator);
    // defer events.deinit();

    // try r.read(std.ArrayList, &events, .{ .logical_txn = 0 });
}
