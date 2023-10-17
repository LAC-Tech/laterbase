// Inter-replica data - that is to say data sent between replicas

const time = @import("time.zig");

// A unique ID for a replica, and a destination to send messages to
// TODO: heap allocated bytes won't work across a network
pub const Address = struct { bytes: []u8 };

// Not inside Event struct because an ID is not generic
// TODO: move back in when an Event is no longer generic??
pub const EventId = packed struct {
    physical_time: i48,
    randomness: u80,

    pub fn init(physical_time: i64, randomness: u80) @This() {
        return .{
            .physical_time = @truncate(physical_time),
            .randomness = randomness,
        };
    }

    comptime {
        if (@sizeOf(EventId) != 16) {
            @compileError("Event Id is not 16 bytes");
        }
    }
};

pub fn Event(comptime Payload: type) type {
    return struct { id: EventId, origin: Address, payload: Payload };
}

// Receiving these messages must be idempotent
pub fn Message(comptime Payload: type) type {
    return union(enum) {
        replicate_from: Address,
        send_since: struct { counter: time.Counter, src: Address },
        store: struct {
            events: []const Event(Payload),
            until: time.Counter,
            src: Address,
        },
        store_new: []const .{ EventId, Payload },
    };
}
