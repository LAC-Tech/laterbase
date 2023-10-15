// All inter-replica data

// A unique ID for a replica, and a destination to send messages to
// TODO: heap allocated bytes won't work across a network
pub const Address = packed struct { bytes: []u8 };
