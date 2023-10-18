const std = @import("std");
const ds = @import("ds.zig");
const net = @import("net.zig");
const time = @import("time.zig");

const Query = union(time.Type) {
    logical_txn: u64,
    physical_valid: i64,
};

pub fn Local(comptime Payload: type) type {
    const Event = net.Event(Payload);
    const Msg = net.Message(Payload);
    const Send = *const fn (msg: Msg, addr: net.Address) void;
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
        send: Send,
        allocator: std.mem.Allocator,

        fn init(
            allocator: std.mem.Allocator,
            addr: net.Address,
            send: Send,
        ) !@This() {
            return .{
                .log = try Log.initCapacity(allocator, maxNumEvents),
                .id_index = try IdIndex.init(allocator, maxNumEvents),
                .logical_clock = try time.LogicalClock.init(allocator),
                .addr = addr,
                .send = send,
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

        fn recv(self: *@This(), msg: Msg, comptime send_msg: anytype) void {
            // TODO: one buffer for whole replica? use different allocation?
            var msg_event_buf = std.ArrayList(Event).init(self.allocator);
            defer msg_event_buf.deinit();

            switch (msg) {
                .replicate_from => |dest_addr| {
                    self.send_msg(
                        .{
                            .src = self.addr,
                            .counter = self.logical_clock.get(dest_addr),
                        },
                        dest_addr,
                    );
                },
                .send_since => |value| {
                    const events_to_send = self.log.items[value.counter.raw..];
                    msg_event_buf.appendSlice(events_to_send);

                    send_msg(.{
                        .events = msg_event_buf.toOwnedSlice(),
                        .until = .{ .raw = self.log.slice.len },
                        .src = self.addr,
                    }, value.src);
                },
                else => @panic("TODO: implement me"),
            }
        }
    };
}
