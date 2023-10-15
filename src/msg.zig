// All inter-replica data

const time = @import("time.zig");

// A unique ID for a replica, and a destination to send messages to
// TODO: heap allocated bytes won't work across a network
pub const Address = struct { bytes: []u8 };

pub const Event = struct {
    pub const Id = packed struct {
        physical_time: u48,
        randomness: u80,

        pub fn init(physical_time: i64, rand_bytes: [10]u8) @This() {
            var signed_time: u64 = @intCast(physical_time);

            return .{
                .timestamp = @truncate(signed_time),
                .randomness = @as(u80, @bitCast(rand_bytes)),
            };
        }
    };

    comptime {
        if (@sizeOf(Id) != 16) {
            @compileError("Event Id is not 16 bytes");
        }
    }

    pub fn Val(comptime Payload: type) type {
        return struct {
            payload: Payload,
            origin: Address,

            pub fn init(payload: Payload, origin: Address) @This() {
                return .{ .payload = payload, .origin = origin };
            }
        };
    }
};

// Receiving these messages must be idempotent
pub fn Message(comptime Payload: type) type {
    return union(enum) {
        replicate_from: Address,
        send_since: struct { counter: time.Counter, src: Address },
        store: struct {
            events: []const .{ Event.Id, Event.Val(Payload) },
            until: time.Counter,
            src: Address,
        },
        store_new: []const .{ Event.Id, Payload },
    };
}
