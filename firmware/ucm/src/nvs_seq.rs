//! Persists the record sequence high-water mark in NVS (`bridge_core::seq`).

use bridge_core::seq::SeqStore;
use esp_idf_svc::nvs::{EspDefaultNvsPartition, EspNvs, NvsDefault};

const NAMESPACE: &str = "ecoport";
const KEY: &str = "seq_hwm";

pub struct NvsSeqStore {
    nvs: EspNvs<NvsDefault>,
}

impl NvsSeqStore {
    /// Opens (creating if needed) the `ecoport` namespace in the default NVS partition.
    pub fn new(partition: EspDefaultNvsPartition) -> anyhow::Result<Self> {
        Ok(Self {
            nvs: EspNvs::new(partition, NAMESPACE, true)?,
        })
    }
}

impl SeqStore for NvsSeqStore {
    fn load(&mut self) -> Option<u32> {
        match self.nvs.get_u32(KEY) {
            Ok(mark) => mark,
            Err(e) => {
                log::warn!("Couldn't read {NAMESPACE}/{KEY} from NVS: {e}");
                None
            }
        }
    }

    fn save(&mut self, high_water_mark: u32) -> anyhow::Result<()> {
        // `set_u32` commits, so the mark survives a reset straight after.
        self.nvs.set_u32(KEY, high_water_mark)?;
        Ok(())
    }
}
